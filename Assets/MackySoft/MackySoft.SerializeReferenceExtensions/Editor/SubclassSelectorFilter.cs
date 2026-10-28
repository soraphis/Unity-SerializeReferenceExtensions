using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MackySoft.SerializeReferenceExtensions.Editor
{
    internal static class SubclassSelectorFilter
    {

        /// <summary>Runs <see cref="SubclassSelectorAttribute.FilterMethod"/> on the object declaring the field. Falls back to all types on any problem.</summary>
        public static IReadOnlyList<Type> Apply (SerializedProperty property, string methodName, IReadOnlyList<Type> types)
        {
            if (string.IsNullOrEmpty(methodName))
            {
                return types;
            }

            try
            {
                object owner = GetDeclaringObject(property);
                MethodInfo method = owner != null ? FindMethod(owner.GetType(), methodName) : null;
                if (method == null)
                {
                    Debug.LogWarning($"[SubclassSelector] Filter method '{methodName}' not found on the object declaring '{property.propertyPath}'. Showing all types.");
                    return types;
                }

                var result = method.Invoke(method.IsStatic ? null : owner, new object[] { types }) as IEnumerable<Type>;
                return result != null ? result.ToList() : types;
            }
            catch (Exception e)
            {
                string message = e is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException.Message : e.Message;
                Debug.LogWarning($"[SubclassSelector] Filter method '{methodName}' failed ({message}). Showing all types.");
                return types;
            }
        }

        private static MethodInfo FindMethod (Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (; type != null; type = type.BaseType)
            {
                MethodInfo method = type.GetMethod(name, flags, null, new[] { typeof(IEnumerable<Type>) }, null);
                if (method != null)
                {
                    return method;
                }
            }
            return null;
        }

        // For a list element, the declaring object is the owner of the list field.
        private static object GetDeclaringObject (SerializedProperty property)
        {
            string[] parts = property.propertyPath.Split('.');
            int count = parts.Length;

            if (count >= 2 && parts[count - 2] == "Array" && parts[count - 1].StartsWith("data["))
            {
                count -= 2;
            }
            count--; // exclude the field itself

            object obj = property.serializedObject.targetObject;
            for (int i = 0; i < count && obj != null; i++)
            {
                if (parts[i] == "Array" && i + 1 < count && parts[i + 1].StartsWith("data["))
                {
                    string token = parts[i + 1];
                    int index = int.Parse(token.Substring(5, token.Length - 6));
                    obj = obj is IList list && index < list.Count ? list[index] : null;
                    i++;
                    continue;
                }

                obj = GetFieldValue(obj, parts[i]);
            }
            return obj;
        }

        private static object GetFieldValue (object obj, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (Type type = obj.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, flags);
                if (field != null)
                {
                    return field.GetValue(obj);
                }
            }
            return null;
        }

    }
}
