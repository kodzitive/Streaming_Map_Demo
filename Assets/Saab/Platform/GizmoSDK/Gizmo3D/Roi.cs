﻿//******************************************************************************
//
// Copyright (C) SAAB AB
//
// All rights, including the copyright, to the computer program(s)
// herein belong to SAAB AB. The program(s) may be used and/or
// copied only with the written permission of SAAB AB, or in
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
// File			: Roi.cs
// Module		: Gizmo3D C#
// Description	: C# Bridge to gzRoi class
// Author		: Anders Modén		
// Product		: Gizmo3D 2.10.6
//		
//
//			
// NOTE:	Gizmo3D is a high performance 3D Scene Graph and effect visualisation 
//			C++ toolkit for Linux, Mac OS X, Windows, Android, iOS and HoloLens for  
//			usage in Game or VisSim development.
//
//
// Revision History...							
//									
// Who	Date	Description						
//									
// AMO	180301	Created file 	
//
//******************************************************************************

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GizmoSDK.GizmoBase;


namespace GizmoSDK
{
    namespace Gizmo3D
    {
        public class Roi : Transform
        {
            public Roi(IntPtr nativeReference) : base(nativeReference) { }

            public Roi(string name = "") : base(Roi_create(name)) { }

            public new static void InitializeFactory()
            {
                AddFactory(new Roi());
            }

            public static new void UninitializeFactory()
            {
                RemoveFactory("gzRoi");
            }

            public override Reference Create(IntPtr nativeReference)
            {
                return new Roi(nativeReference) as Reference;
            }

            public RoiNode GetClosestRoiNode(Vec3D position)
            {
                RoiNode node;

                // We must call this from a node locked state as childs might unload during other edits

                try
                {
                    NodeLock.WaitLockEdit();

                    node = CreateObject(Roi_getClosestRoiNode(GetNativeReference(), ref position)) as RoiNode;
                }
                finally
                {
                    NodeLock.UnLock();
                }

                return node;
            }

            public Vec3D Position
            {
                get
                {
                    Vec3D result = new Vec3D();

                    Roi_getPosition(GetNativeReference(), ref result);

                    return result;
                }

                set
                {
                    Roi_setPosition(GetNativeReference(), ref value);
                }
            }

            #region Native dll interface ----------------------------------
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern IntPtr Roi_create(string name);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern IntPtr Roi_getClosestRoiNode(IntPtr roi_reference , ref Vec3D position);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern void Roi_getPosition(IntPtr roi_reference, ref Vec3D position);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern void Roi_setPosition(IntPtr roi_reference, ref Vec3D position);
            #endregion
        }
    }
}
