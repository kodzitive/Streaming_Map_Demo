﻿//******************************************************************************
//
// Copyright (C) SAAB AB
//
// All rights, including the copyright, to the computer program(s)
// herein belong to SAAB AB. The program(s) may be used and/or
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
// File			: DistObject.cs
// Module		: GizmoDistribution C#
// Description	: C# Bridge to gzDistObject class
// Author		: Anders Modén		
// Product		: GizmoDistribution 2.10.6
//		
//
//			
// NOTE:	GizmoBase is a platform abstraction utility layer for C++. It contains 
//			design patterns and C++ solutions for the advanced programmer.
//
//
// Revision History...							
//									
// Who	Date	Description						
//									
// AMO	180301	Created file 
// AMO  181210  Added Concurrent reading of dictionary
//
//******************************************************************************

using System;
using System.Runtime.InteropServices;
using GizmoSDK.GizmoBase;
using System.Collections.Concurrent;


namespace GizmoSDK
{
    namespace GizmoDistribution
    {
        public class DistObject : Reference
        {
            public DistObject(IntPtr nativeReference) : base(nativeReference)
            {
                ReferenceDictionary<DistObject>.AddObject(this);
            }
    
            public DistObject(string name):base(DistObject_createDefaultObject(name))
            {
                ReferenceDictionary<DistObject>.AddObject(this);
            }

            override public void Release()
            {
                ReferenceDictionary<DistObject>.RemoveObject(this);
                base.Release();
            }


            public string GetName()
            {
                return Marshal.PtrToStringUni(DistObject_getName(GetNativeReference()));
            }

            public bool SetAttributeValue(string name, DynamicType value)
            {
                return DistObject_setAttributeValue(GetNativeReference(), name, value.GetNativeReference());
            }

            public bool SetAttributeValues(DistTransaction transaction)
            {
                return DistObject_setAttributeValues(GetNativeReference(), transaction.GetNativeReference());
            }

            public bool RemoveAttribute(string name)
            {
                return DistObject_removeAttribute(GetNativeReference(), name);
            }

            public DynamicType GetAttributeValue(string name)
            {
                var value = DistObject_getAttributeValue(GetNativeReference(), name);
                return value == IntPtr.Zero ? null : new DynamicType(value);
            }

            public bool HasAttribute(string name)
            {
                return DistObject_hasAttribute(GetNativeReference(), name);
            }

            public bool InSession()
            {
                return DistObject_inSession(GetNativeReference());
            }

            public static void InitializeFactory()
            {
                AddFactory(new DistObject("factory"));
            }

            public static void UninitializeFactory()
            {
                RemoveFactory("gzDistObject");
            }

            public override Reference Create(IntPtr nativeReference)
            {
                return new DistObject(nativeReference) as Reference;
            }
                        
            public override string ToString()
            {
                return Marshal.PtrToStringUni(DistObject_asString(GetNativeReference()));
            }

            public string ToJSON()
            {
                return Marshal.PtrToStringUni(DistObject_asJSON(GetNativeReference()));
            }

            public bool RestoreFromXML(string xml)
            {
                return DistObject_fromXML(GetNativeReference(), xml);
            }

            public bool RestoreFromJSON(string json)
            {
                return DistObject_fromJSON(GetNativeReference(), json);
            }

            // --- Reflection mechanisms --------------------------------

            static public bool StorePropertiesAndFields(DistObject distobj, object obj, bool allProperties = false)
            {
                foreach (System.Reflection.PropertyInfo prop in obj.GetType().GetProperties())
                {
                    if (allProperties || Attribute.IsDefined(prop, typeof(DistProperty)))
                        if (!distobj.SetAttributeValue(prop.Name, DynamicType.CreateDynamicType(prop.GetValue(obj), allProperties)))
                            return false;
                }

                foreach (System.Reflection.FieldInfo field in obj.GetType().GetFields())
                {
                    if (allProperties || Attribute.IsDefined(field, typeof(DistProperty)))
                        if (!distobj.SetAttributeValue(field.Name, DynamicType.CreateDynamicType(field.GetValue(obj), allProperties)))
                            return false;
                }

                return true;
            }

            static public void RestorePropertiesAndFields(DistObject distobj, object obj, bool allProperties = false)
            {
                foreach (System.Reflection.PropertyInfo prop in obj.GetType().GetProperties())
                {
                    if (allProperties || Attribute.IsDefined(prop, typeof(DistProperty)))
                        prop.SetValue(obj, distobj.GetAttributeValue(prop.Name).GetObject(prop.PropertyType, allProperties));
                }

                foreach (System.Reflection.FieldInfo field in obj.GetType().GetFields())
                {
                    if (allProperties || Attribute.IsDefined(field, typeof(DistProperty)))
                        field.SetValue(obj, distobj.GetAttributeValue(field.Name).GetObject(field.FieldType, allProperties));
                }
            }

            #region --------------------------- private ----------------------------------------------

            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern bool DistObject_fromXML(IntPtr event_reference, string xml);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern bool DistObject_fromJSON(IntPtr event_reference, string json);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern IntPtr DistObject_createDefaultObject(string name);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern bool DistObject_setAttributeValue(IntPtr event_reference,string name,IntPtr dynamic_reference);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern bool DistObject_setAttributeValues(IntPtr object_reference, IntPtr transaction_reference);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern bool DistObject_removeAttribute(IntPtr event_reference, string name);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern IntPtr DistObject_getAttributeValue(IntPtr event_reference, string name);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern bool DistObject_hasAttribute(IntPtr event_reference, string name);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern IntPtr DistObject_getName(IntPtr object_reference);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern IntPtr DistObject_asString(IntPtr event_reference);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern IntPtr DistObject_asJSON(IntPtr event_reference);
            [DllImport(Platform.BRIDGE, CharSet = CharSet.Unicode, CallingConvention = CallingConvention.Cdecl)]
            private static extern bool DistObject_inSession(IntPtr object_reference);

            #endregion
        }
    }
}
