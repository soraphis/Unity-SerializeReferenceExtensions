using System;
using UnityEngine;

/// <summary>
/// Attribute to specify the type of the field serialized by the SerializeReference attribute in the inspector.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
public sealed class SubclassSelectorAttribute : PropertyAttribute
{

#if UNITY_2021_3_OR_NEWER
    // NOTE: Use managedReferenceValue getter to invoke instance method in SubclassSelectorDrawer.
    public bool UseToStringAsLabel { get; set; }
#endif

    /// <summary>
    /// Name of a method on the object that declares the field (instance or static, any visibility) used to
    /// filter the types offered in the type popup. Signature: <c>IEnumerable&lt;Type&gt; Method (IEnumerable&lt;Type&gt; candidates)</c>.
    /// Use <c>nameof(...)</c>. Editor-only: evaluated when the popup opens.
    /// </summary>
    public string FilterMethod { get; set; }

}