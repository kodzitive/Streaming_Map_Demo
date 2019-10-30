﻿//******************************************************************************
//
// Copyright (C) SAAB AB
//
// All rights, including the copyright, to the computer program(s) 
// herein belong to Saab AB. The program(s) may be used and/or
// copied only with the written permission of Saab AB, or in
// accordance with the terms and conditions stipulated in the
// agreement/contract under which the program(s) have been
// supplied. 
//
//
// Information Class:	COMPANY UNCLASSIFIED
// Defence Secrecy:		NOT CLASSIFIED
// Export Control:		NOT EXPORT CONTROLLED
//
//
// File			: SceneManager.cs
// Module		:
// Description	: Management of dynamic asset loader from GizmoSDK
// Author		: Anders Modén
// Product		: Gizmo3D 2.10.1
//
// NOTE:	Gizmo3D is a high performance 3D Scene Graph and effect visualisation 
//			C++ toolkit for Linux, Mac OS X, Windows (Win32) and IRIX® for  
//			usage in Game or VisSim development.
//
//
// Revision History...
//
// Who	Date	Description
//
// AMO	180607	Created file        (2.9.1)
//
//******************************************************************************

// ************************** NOTE *********************************************
//
//      Stand alone from BTA !!! No BTA code in this !!!
//
// *****************************************************************************

// Unity Managed classes
using UnityEngine;

// Gizmo Managed classes
using GizmoSDK.GizmoBase;
using GizmoSDK.Gizmo3D;
using GizmoSDK.Coordinate;

// Fix some conflicts between unity and Gizmo namespaces
using gzCamera = GizmoSDK.Gizmo3D.Camera;
using gzTransform = GizmoSDK.Gizmo3D.Transform;
using gzTexture = GizmoSDK.Gizmo3D.Texture;
using gzImage = GizmoSDK.GizmoBase.Image;


// Map utility
using Saab.Foundation.Map;
using Saab.Unity.Extensions;
using Saab.Utility.Unity.NodeUtils;

// Fix unity conflicts
using unTransform = UnityEngine.Transform;

// System
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System;
using System.Collections;
using UnityEngine.Networking;
using Saab.Unity.Core;
using Saab.Core;

namespace Saab.Foundation.Unity.MapStreamer
{
    // The SceneManager behaviour takes a unity camera and follows that to populate the current scene with GameObjects in a scenegraph hierarchy

    public class SceneManager : BtaComponent
    {

        public UnityEngine.Camera   UnityCamera;
        public UnityEngine.Shader   DefaultShader;
        [Header("CrossBoards")]
        public UnityEngine.Shader   CrossboardShader;
        public UnityEngine.ComputeShader ComputeShader;
        [Space(height:20)]
        public string               MapUrl;
        public int                  FrameCleanupInterval = 1000;
        public double               MaxBuildTime = 0.01;   // 100hz

        public delegate void SceneManager_OnUpdate(SceneManager sender);
        public event SceneManager_OnUpdate OnUpdateScene;

        #region ------------- Privates ----------------

        private Scene _native_scene;
        private gzCamera _native_camera;
        private Context _native_context;
        private CullTraverseAction _native_traverse_action;
        private GameObject _root;

        private NodeAction _actionReceiver;
        private Matrix4x4 _zflipMatrix;

        private int _unusedCounter = 0;
  
        private readonly string ID = "Saab.Foundation.Unity.MapStreamer.SceneManager";

        //#pragma warning disable 414
        //private UnityPluginInitializer _plugin_initializer;
        //#pragma warning restore 414

        #endregion

        struct NodeLoadInfo
        {
            public NodeLoadInfo(DynamicLoadingState _state, DynamicLoader _loader, Node _node)
            {
                state = _state;
                loader = _loader;
                node = _node;
            }

            public DynamicLoadingState state;
            public DynamicLoader loader;
            public Node node;

        };

        // The activation struct carries information about the state of a native node (loaded/unloaded)
        struct ActivationInfo
        {
            public ActivationInfo(NodeActionEvent _state, Node _node)
            {
                state = _state;
                node = _node;
            }

            public NodeActionEvent state;
            public Node node;
        };

        // GameObjectInfo carries info about added objects to scene
        struct GameObjectInfo
        {
            public GameObjectInfo(NodeLoadInfo _info, GameObject _go)
            {
                nodeInfo = _info;
                go = _go;
            }

            public NodeLoadInfo nodeInfo;
            public GameObject go;

        };

        // AssetLoadInfo carries info about loading an asset
        struct AssetLoadInfo
        {
            public AssetLoadInfo(GameObject _parent, string _res_url, string _obj_id)
            {
                parent = _parent;
                resource_url = _res_url;
                object_id = _obj_id;
            }

            public GameObject parent;
            public string resource_url;
            public string object_id;
        };

        // A queue for new pending loads/unloads
        List<NodeLoadInfo> pendingLoaders = new List<NodeLoadInfo>(100);
        // A queue for pending activations/deactivations
        List<ActivationInfo> pendingActivations = new List<ActivationInfo>(100);
        // A queue for pending new objects
        List<GameObjectInfo> pendingObjects = new List<GameObjectInfo>(100);

        // A queue for post build work
        Queue<NodeHandle> pendingBuilds = new Queue<NodeHandle>(500);

        // A queue for AssetLoading
        Stack<AssetLoadInfo> pendingAssetLoads = new Stack<AssetLoadInfo>(100);

        // The current active asset bundles
        Dictionary<string, AssetBundle> currentAssetBundles = new Dictionary<string, AssetBundle>();

        // Lookup for used materials
        Dictionary<IntPtr, Material> textureMaterialStorage = new Dictionary<IntPtr, Material>();

        // Linked List for nodes that needs updates on update
        LinkedList<GameObject> updateNodeObjects = new LinkedList<GameObject>();

        //Add GameObjects to dictionary

        // Traverse function iterates the scene graph to build local branches on Unity
        GameObject Traverse(Node n, Material currentMaterial)
        {
            if (n == null || !n.IsValid())
                return null;

            string name = n.GetName();

            if (String.IsNullOrEmpty(name))
                name = n.GetNativeTypeName();

            GameObject gameObject = new GameObject(name);

            var nodeHandle = gameObject.AddComponent<NodeHandle>();

            //nodeHandle.Renderer = Renderer;
            nodeHandle.node = n;
            nodeHandle.inObjectDict = false;
            nodeHandle.currentMaterial = currentMaterial;
            nodeHandle.ComputeShader = ComputeShader;

            // ---------------------------- Check material state ----------------------------------

            if (n.HasState())
            {
                State state = n.State;

                if (state.HasTexture(0) && state.GetMode(StateMode.TEXTURE) == StateModeActivation.ON)
                {
                    gzTexture texture = state.GetTexture(0);

                    if (!textureMaterialStorage.TryGetValue(texture.GetNativeReference(), out currentMaterial))
                    {
                        if (texture.HasImage())
                        {
                            gzImage image = texture.GetImage();

                            int depth = (int)image.GetDepth();
                            int width = (int)image.GetWidth();
                            int height = (int)image.GetHeight();

                            bool can_create_mipmaps = false;

                            if (depth == 1)
                            {

                                if (n is Crossboard)
                                    currentMaterial = new Material(CrossboardShader);
                                else
                                    currentMaterial = new Material(DefaultShader);

                                TextureFormat format = TextureFormat.ARGB32;

                                ImageType image_type = image.GetImageType();

                                switch (image_type)
                                {
                                    case ImageType.RGB_8_DXT1:
                                        format = TextureFormat.DXT1;
                                        break;

                                    case ImageType.RGBA_8_DXT5:
                                        format = TextureFormat.DXT5;
                                        break;

                                    case ImageType.RGBA_8:
                                        format = TextureFormat.RGBA32;
                                        can_create_mipmaps = true;
                                        break;

                                    case ImageType.RGB_8:
                                        format = TextureFormat.RGB24;
                                        can_create_mipmaps = true;
                                        break;

                                    default:
                                        // Issue your own error here because we can not use this texture yet
                                        return null;
                                }

                                Texture2D tex = new Texture2D(width, height, format, false);

                                byte[] image_data;

                                image.GetImageArray(out image_data);

                                tex.LoadRawTextureData(image_data);

                                if (texture.UseMipMaps && can_create_mipmaps)
                                {
                                    Texture2D tex2 = new Texture2D(width, height, format, true);
                                    tex2.SetPixels(tex.GetPixels(0, 0, tex.width, tex.height));

                                    tex = tex2;
                                }


                                switch (texture.MinFilter)
                                {
                                    default:
                                        tex.filterMode = FilterMode.Point;
                                        break;

                                    case gzTexture.TextureMinFilter.LINEAR:
                                    case gzTexture.TextureMinFilter.LINEAR_MIPMAP_NEAREST:
                                        tex.filterMode = FilterMode.Bilinear;
                                        break;

                                    case gzTexture.TextureMinFilter.LINEAR_MIPMAP_LINEAR:
                                        tex.filterMode = FilterMode.Trilinear;
                                        break;
                                }

                                tex.Apply(texture.UseMipMaps, true);

                                currentMaterial.mainTexture = tex;

                            }



                            image.Dispose();
                        }

                        // Add some kind of check for textures shared by many
                        // Right now only for crossboards

                        if (n is Crossboard)
                            textureMaterialStorage.Add(texture.GetNativeReference(), currentMaterial);

                    }

                    nodeHandle.currentMaterial = currentMaterial;
                    texture.Dispose();
                }

                state.Dispose();
            }

            // ---------------------------- Transform check -------------------------------------

            gzTransform tr = n as gzTransform;

            if (tr != null)
            {
                Vec3 translation;

                if (tr.GetTranslation(out translation))
                {
                    Vector3 trans = new Vector3(translation.x, translation.y, translation.z);
                    gameObject.transform.localPosition = trans;
                }

            }

            // ---------------------------- DynamicLoader check -------------------------------------

            DynamicLoader dl = n as DynamicLoader;

            if (dl != null)
            {
                if (!nodeHandle.inObjectDict)
                {
                    NodeUtils.AddGameObjectReference(dl.GetNativeReference(), gameObject);
                    nodeHandle.inObjectDict = true;
                }
                else
                    return gameObject;
            }

            // ---------------------------- Lod check -------------------------------------

            Lod ld = n as Lod;

            if (ld != null)
            {
                foreach (Node child in ld)
                {
                    GameObject go_child = Traverse(child, currentMaterial);

                    if (go_child == null)
                        return null;

                    NodeHandle h = go_child.GetComponent<NodeHandle>();

                    if (h != null)
                    {
                        if (!h.inObjectDict)
                        {
                            NodeUtils.AddGameObjectReference(h.node.GetNativeReference(), go_child);
                            h.node.AddActionInterface(_actionReceiver, NodeActionEvent.IS_TRAVERSABLE);
                            h.node.AddActionInterface(_actionReceiver, NodeActionEvent.IS_NOT_TRAVERSABLE);
                            h.inObjectDict = true;
                        }

                    }

                    go_child.transform.SetParent(gameObject.transform, false);
                }

                // Dont process group
                return gameObject;
            }

            // ---------------------------- Roi check -------------------------------------

            Roi roi = n as Roi;

            if (roi != null)
            {
                nodeHandle.updateTransform = true;
                nodeHandle.inNodeUpdateList = true;
                updateNodeObjects.AddLast(gameObject);

                foreach (Node child in roi)
                {
                    GameObject go_child = Traverse(child, currentMaterial);

                    if (go_child == null)
                        return null;

                    NodeHandle h = go_child.GetComponent<NodeHandle>();

                    if (h != null)
                    {
                        if (!h.inObjectDict)
                        {
                            NodeUtils.AddGameObjectReference(h.node.GetNativeReference(), go_child);
                            h.node.AddActionInterface(_actionReceiver, NodeActionEvent.IS_TRAVERSABLE);
                            h.node.AddActionInterface(_actionReceiver, NodeActionEvent.IS_NOT_TRAVERSABLE);
                            h.inObjectDict = true;
                        }

                    }

                    go_child.transform.SetParent(gameObject.transform, false);
                }

                // Dont process group
                return gameObject;
            }

            // ---------------------------- RoiNode check -------------------------------------

            RoiNode roinode = n as RoiNode;

            if (roinode != null)
            {
                nodeHandle.updateTransform = true;
                nodeHandle.inNodeUpdateList = true;
                updateNodeObjects.AddLast(gameObject);
            }

            // ---------------------------- Group check -------------------------------------

            Group g = n as Group;

            if (g != null)
            {

                foreach (Node child in g)
                {
                    GameObject go_child = Traverse(child, currentMaterial);

                    if (go_child == null)
                        return null;

                    go_child.transform.SetParent(gameObject.transform, false);
                }

                return gameObject;
            }

            // ---------------------------ExtRef check -----------------------------------------

            ExtRef ext = n as ExtRef;

            if (ext != null)
            {
                AssetLoadInfo info = new AssetLoadInfo(gameObject, ext.ResourceURL, ext.ObjectID);

                pendingAssetLoads.Push(info);
            }

            // ---------------------------- Crossboard check -----------------------------------

            Crossboard cb = n as Crossboard;

            if (cb != null)
            {
                // Scheduled for later build
                pendingBuilds.Enqueue(nodeHandle);
            }

            // ---------------------------- Geometry check -------------------------------------

            Geometry geom = n as Geometry;

            if (geom != null)
            {
                nodeHandle.BuildGameObject();

                // Latron we will identify types of geoemtry that will be scheduled later if they are extensive and not ground
                //// Scheduled for later build
                //pendingBuilds.Enqueue(nodeHandle);
            }

            return gameObject;
        }

        private IEnumerator AssetLoader()
        {
            while (true)
            {
                if (pendingAssetLoads.Count > 0)
                {
                    AssetLoadInfo info = pendingAssetLoads.Pop();

                    AssetBundle assetBundle;

                    if (!currentAssetBundles.TryGetValue(info.resource_url, out assetBundle))
                    {
                        UnityEngine.Networking.UnityWebRequest request = UnityWebRequestAssetBundle.GetAssetBundle(info.resource_url, 0);

                        yield return request.SendWebRequest();

                        assetBundle = DownloadHandlerAssetBundle.GetContent(request);

                        if (assetBundle)
                            currentAssetBundles.Add(info.resource_url, assetBundle);
                    }

                    if (assetBundle != null)
                    {

                        GameObject extRefObject = assetBundle.LoadAsset<GameObject>(info.object_id);

                        if (extRefObject != null)
                        {
                            GameObject instance = Instantiate(extRefObject);

                            if (instance != null)
                            {
                                instance.name = info.object_id;
                                instance.transform.SetParent(info.parent.transform, false);
                            }
                        }
                    }
                }

                yield return null;
            }
        }

        // The LoadMap function takes an URL and loads the map into GizmoSDK native db
        public bool LoadMap(string mapURL)
        {
            NodeLock.WaitLockEdit();      // We assume we do all editing from main thread and to allow render we assume we edit in edit mode

            try // We are now locked in edit
            {

                if (!ResetMap())
                    return false;

                var node = DbManager.LoadDB(mapURL);

                if (node == null || !node.IsValid())
                {
                    Message.Send(ID, MessageLevel.WARNING, $"Failed to load map {mapURL}");
                    return false;
                }

                MapUrl = mapURL;

                MapControl.SystemMap.NodeURL = mapURL;
                MapControl.SystemMap.CurrentMap = node;

                _native_scene.AddNode(MapControl.SystemMap.CurrentMap);
                
                _native_scene.Debug();

                _root = new GameObject("root");

                GameObject scene = Traverse(MapControl.SystemMap.CurrentMap, null);

                if(scene!=null)
                    scene.transform.SetParent(_root.transform, false);

                // As GizmoSDK has a flipped Z axis going out of the screen we need a top transform to flip Z
                _root.transform.localScale = new Vector3(1, 1, -1);


                //// Add example object under ROI --------------------------------------------------------------

                //MapPos mapPos;

                //GetMapPosition(new LatPos(1.0084718541, 0.24984267815, 300), out mapPos, GroundClampType.GROUND, true);

                //_test = GameObject.CreatePrimitive(PrimitiveType.Sphere);

                //_test.transform.parent = FindFirstGameObjectTransform(mapPos.roiNode);
                //_test.transform.localPosition = mapPos.position.ToVector3();
                //_test.transform.localScale = new Vector3(10, 10, 10);

                //// ------------------------------------------------------------------------------------------

            }
            finally
            {
                NodeLock.UnLock();
            }

            return true;
        }

        public bool ResetMap()
        {
            NodeLock.WaitLockEdit();

            try // We are now locked in edit
            {

                foreach (var p in pendingLoaders)
                {
                    p.loader?.Dispose();
                    p.node?.Dispose();
                }

                pendingLoaders.Clear();

                foreach (var p in pendingActivations)
                {
                    p.node?.Dispose();
                }

                pendingActivations.Clear();

                RemoveGameObjectHandles(_root);

                GameObject.Destroy(_root);

                _root = null;

                MapControl.SystemMap.Reset();
            }
            finally
            {
                NodeLock.UnLock();
            }

            return true;
        }


        public bool InitializeInternal()        //Initialize()
        {
            _actionReceiver = new NodeAction("DynamicLoadManager");

            _actionReceiver.OnAction += ActionReceiver_OnAction;

            _zflipMatrix = new Matrix4x4(new Vector4(1, 0, 0), new Vector4(0, 1, 0), new Vector4(0, 0, -1), new Vector4(0, 0, 0, 1));

            GizmoSDK.GizmoBase.Message.Send("SceneManager", MessageLevel.DEBUG, "Loading Graph");

            NodeLock.WaitLockEdit();

            try // We are now locked in edit
            {

                _native_camera = new PerspCamera("Test");
                _native_camera.RoiPosition = true;
                MapControl.SystemMap.Camera = _native_camera;

                _native_scene = new Scene("TestScene");

                _native_context = new Context();

                //_native_camera.Debug(_native_context);      // Enable to debug view

                _native_traverse_action = new CullTraverseAction();

                DynamicLoader.OnDynamicLoad += DynamicLoader_OnDynamicLoad;

                _native_camera.Scene = _native_scene;
            }
            finally
            {

                NodeLock.UnLock();
            }


            DynamicLoaderManager.StartManager();

            return true;
        }

        public bool Uninitialize()
        {

            DynamicLoaderManager.StopManager();

            ResetMap();

            DynamicLoader.OnDynamicLoad -= DynamicLoader_OnDynamicLoad;
            _actionReceiver.OnAction -= ActionReceiver_OnAction;

            NodeLock.WaitLockEdit();

            try // We are now locked in edit
            {

                _native_camera.Debug(_native_context, false);
                _native_camera.Dispose();
                _native_camera = null;

                _native_context.Dispose();
                _native_context = null;

                _native_scene.Dispose();
                _native_scene = null;


                _actionReceiver.Dispose();
                _actionReceiver = null;

            }
            finally
            {
                NodeLock.UnLock();
            }


            return true;
        }

        protected override bool CheckDependencies()
        {
            return true;
        }

        protected override IEnumerator InitComponent(Action<bool> success)
        {
            StartCoroutine(AssetLoader());
            MapUrl = KeyDatabase.GetDefaultUserKey("SceneManager/MapUrl", MapUrl);

            InitMap();

            success(true);
            yield break;
        }

        private void ActionReceiver_OnAction(NodeAction sender, NodeActionEvent action, Context context, NodeActionProvider trigger, TraverseAction traverser, IntPtr userdata)
        {
            // Locked in edit or render (render) by caller

            if ((action == NodeActionEvent.IS_TRAVERSABLE) || (action == NodeActionEvent.IS_NOT_TRAVERSABLE))
            {
                pendingActivations.Add(new ActivationInfo(action, trigger as Node));
            }
            else
                trigger?.ReleaseNoDelete();      // We are getting ref counts on objects in scene graph and these need to be released immediately

            traverser?.ReleaseNoDelete();
            context?.ReleaseNoDelete();
        }

        protected override void OnAwake()
        {
            UnityPlugin_Initialize();
        }

        private void OnEnable()
        {
            if (!Initialized)
            {
                return;
            }
            InitMap();
        }


        private void OnDisable()
        {
            // We add Unitialize and shut down threads here as this routine gets called by an edit in code
            Uninitialize();
        }
        private void InitMap()
        {
            //_plugin_initializer = new UnityPluginInitializer();  // in case we need it our own
            GizmoSDK.Gizmo3D.Platform.Initialize();

            // Initialize formats
            DbManager.Initialize();

            // Initialize this manager
            InitializeInternal();

            // Load the map
            LoadMap(MapUrl);
        }

       
        private void DynamicLoader_OnDynamicLoad(DynamicLoadingState state, DynamicLoader loader, Node node)
        {
            // Locked in edit or render (render) by caller

            if (state == DynamicLoadingState.LOADED || state == DynamicLoadingState.UNLOADED)
            {
                pendingLoaders.Add(new NodeLoadInfo(state, loader, node));
            }
            else
            {
                loader?.ReleaseNoDelete();      // Same here. We are getting refs to objects in scene graph that we shouldnt release in GC
                node?.ReleaseNoDelete();
            }
        }

        private void RemoveGameObjectHandles(GameObject obj)
        {
            if (obj == null)
                return;

            foreach (unTransform child in obj.transform)
            {
                RemoveGameObjectHandles(child.gameObject);
            }

            NodeHandle h = obj.GetComponent<NodeHandle>();

            if (h != null)
            {
                if (h.inObjectDict)
                {
                    NodeUtils.RemoveGameObjectReference(h.node.GetNativeReference(), obj);
                    h.inObjectDict = false;
                }

                if (h.inNodeUpdateList)
                {
                    updateNodeObjects.Remove(obj);
                    h.inNodeUpdateList = false;
                }

                h.node?.Dispose();
                h.node = null;
            }

        }


        
        private void ProcessPendingUpdates()
        {
            Timer timer = new Timer();      // Measure time precise in update

            #region Dynamic Loading/Add/Remove native handles ---------------------------------------------------------------

            foreach (NodeLoadInfo nodeLoadInfo in pendingLoaders)
            {
                if (nodeLoadInfo.state == DynamicLoadingState.LOADED)
                {
                    unTransform transform = NodeUtils.FindFirstGameObjectTransform(nodeLoadInfo.loader.GetNativeReference());

                    if ((transform != null) && transform.childCount != 0)       // Already have a child
                        continue;

                    GameObject go = Traverse(nodeLoadInfo.node, null);
                    pendingObjects.Add(new GameObjectInfo(nodeLoadInfo, go));
                }
                else if (nodeLoadInfo.state == DynamicLoadingState.UNLOADED)
                {
                    ConcurrentBag<GameObject> bag;

                    if(NodeUtils.FindGameObjects(nodeLoadInfo.loader.GetNativeReference(),out bag))
                    {
                        foreach (GameObject go in bag)
                        {
                            foreach (unTransform child in go.transform)         //We need to unload all limked go in hierarchy
                            {
                                RemoveGameObjectHandles(child.gameObject);
                                GameObject.Destroy(child.gameObject);
                            }
                        }
                    }
                    else
                    {
                        // Obvously we have taken care of data in previsous traversal of a chained update of scenegraph
                        // Message.Send("Unity", MessageLevel.WARNING, "Loader break!!");
                    }
                }

            }

            pendingLoaders.Clear();

            #endregion

            #region Activate/Deactivate GameObjects based on scenegraph -----------------------------------------------------

            foreach (ActivationInfo activationInfo in pendingActivations)
            {
                ConcurrentBag<GameObject> bag;  // We need to activate the correct nodes

                if (NodeUtils.FindGameObjects(activationInfo.node.GetNativeReference(), out bag))
                {
                    foreach (GameObject obj in bag)
                    {
                        if (activationInfo.state == NodeActionEvent.IS_TRAVERSABLE)
                            obj.SetActive(true);
                        else
                            obj.SetActive(false);
                    }
                }
                else
                {
                    Message.Send("Unity", MessageLevel.WARNING, "Activation break!!");
                }
            }

            pendingActivations.Clear();

            #endregion

            #region Creating GameObjects based on traversal -----------------------------------------------------------------

            foreach (GameObjectInfo go_info in pendingObjects)
            {
                ConcurrentBag<GameObject> bag;  // We need to activate the correct nodes

                if (NodeUtils.FindGameObjects(go_info.nodeInfo.loader.GetNativeReference(), out bag))
                {
                    bool shared = false;

                    foreach (GameObject go in bag)      // Possibly multiple gameobjects for shared instances
                    {
                        if (!shared)
                            go_info.go.transform.SetParent(go.transform, false);
                        else
                        {
                            GameObject shared_go = Traverse(go_info.nodeInfo.node, null);
                            shared_go.transform.SetParent(go.transform, false);
                        }
                        shared = true;
                    }
                }
                else
                {
                    RemoveGameObjectHandles(go_info.go);
                    GameObject.Destroy(go_info.go);
                }

            }

            pendingObjects.Clear();

            #endregion

            //Message.Send("SceneManager", MessageLevel.ALWAYS, $"currentObjects Size {currentObjects.Count}");

            //Message.Send("SceneManager", MessageLevel.ALWAYS, String.Format("currentObjects Size {0}", currentObjects.Count));

            #region Update slow loading assets ------------------------------------------------------------------------------

            while (pendingBuilds.Count > 0 && timer.GetTime() < MaxBuildTime)
            {
                NodeHandle handle = pendingBuilds.Dequeue();
                handle.BuildGameObject();
            }

            #endregion

            // Right now we use this as a dirty fix to handle unused shared materials

            _unusedCounter = (_unusedCounter + 1) % FrameCleanupInterval;
            if(_unusedCounter==0)
                Resources.UnloadUnusedAssets();
        }
        
        private void UpdateNodeInternals()
        {
            foreach (GameObject go in updateNodeObjects)
            {
                NodeHandle h = go.GetComponent<NodeHandle>();

                h.UpdateNodeInternals();
            }

            OnUpdateScene?.Invoke(this);
        }

        // Update is called once per frame
        protected override void OnUpdate()
        {
            if (!NodeLock.TryLockEdit(30))      // 30 msek allow latency of other pending editor
                return;

            try // We are now locked in edit
            {
                ProcessPendingUpdates();
            }
            finally
            {
                NodeLock.UnLock();
            }

            if (UnityCamera == null)
                return;

            if (!NodeLock.TryLockRender(30))    // 30 millisek latency allowed
                return;

            try // We are now locked in read
            {
                // Transfer camera parameters

                PerspCamera perspCamera = _native_camera as PerspCamera;

                if (perspCamera != null)
                {
                    perspCamera.VerticalFOV = UnityCamera.fieldOfView;
                    perspCamera.HorizontalFOV = 2 * Mathf.Atan(Mathf.Tan(UnityCamera.fieldOfView * Mathf.Deg2Rad / 2) * UnityCamera.aspect) * Mathf.Rad2Deg; ;
                    perspCamera.NearClipPlane = UnityCamera.nearClipPlane;
                    perspCamera.FarClipPlane = UnityCamera.farClipPlane;
                }

                Matrix4x4 unity_camera_transform = UnityCamera.transform.worldToLocalMatrix;

                Matrix4x4 gz_transform = _zflipMatrix * unity_camera_transform * _zflipMatrix;

                _native_camera.Transform = gz_transform.ToMatrix4();


                var ctrl = UnityCamera.GetComponent<IWorldCoord>();

                if (ctrl != null)
                {
                    var cartpos = ctrl.Coordinate;
                    _native_camera.Position = new Vec3D(cartpos.x, cartpos.y, -cartpos.z);
                }
                           

                _native_camera.Render(_native_context, 1000, 1000, 1000, _native_traverse_action);
                //_native_camera.DebugRefresh();
            }
            finally
            {
                NodeLock.UnLock();
            }

            UpdateNodeInternals();
        }
        

        [DllImport("UnityPluginInterface", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
        private static extern void UnityPlugin_Initialize();
    }

}