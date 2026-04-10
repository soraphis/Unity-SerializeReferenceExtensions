using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MackySoft.SerializeReferenceExtensions.Editor
{

    [CustomPropertyDrawer(typeof(SubclassSelectorAttribute))]
    public class SubclassSelectorDrawer : PropertyDrawer
    {

        private struct TypePopupCache
        {
            public AdvancedTypePopup TypePopup { get; }
            public AdvancedDropdownState State { get; }
            public TypePopupCache (AdvancedTypePopup typePopup, AdvancedDropdownState state)
            {
                TypePopup = typePopup;
                State = state;
            }
        }

        private const int MaxTypePopupLineCount = 13;

        private static readonly GUIContent NullDisplayName = new GUIContent(TypeMenuUtility.NullDisplayName);
        private static readonly GUIContent IsNotManagedReferenceLabel = new GUIContent("The property type is not manage reference.");
        private static readonly GUIContent TempChildLabel = new GUIContent();

        private readonly Dictionary<string, TypePopupCache> typePopups = new Dictionary<string, TypePopupCache>();
        private readonly Dictionary<string, GUIContent> typeNameCaches = new Dictionary<string, GUIContent>();

        private SerializedProperty targetProperty;

        public override VisualElement CreatePropertyGUI (SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                return new Label(IsNotManagedReferenceLabel.text);
            }

            var root = new VisualElement();

            // Header row: expand toggle + property label + type selector button.
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.minHeight = EditorGUIUtility.singleLineHeight;

            var expandToggle = new Toggle();
            expandToggle.AddToClassList("unity-foldout__toggle");
            expandToggle.style.marginLeft = 0;
            expandToggle.style.marginRight = 2;
            expandToggle.style.marginTop = 0;
            expandToggle.style.marginBottom = 0;
            expandToggle.style.flexShrink = 0;

            var propLabel = new Label(property.displayName);
            propLabel.tooltip = property.tooltip;
            propLabel.AddToClassList("unity-property-field__label");
            propLabel.style.flexShrink = 0;

            var typeButton = new Button();
            typeButton.AddToClassList("unity-base-popup-field__input");
            typeButton.style.flexGrow = 1;
            typeButton.style.unityTextAlign = TextAnchor.MiddleLeft;
            typeButton.style.overflow = Overflow.Hidden;
            typeButton.style.marginLeft = 0;
            typeButton.style.marginRight = 0;
            typeButton.style.marginTop = 1;
            typeButton.style.marginBottom = 1;
            typeButton.style.paddingLeft = 4;

            header.Add(expandToggle);
            header.Add(propLabel);
            header.Add(typeButton);

            // Content area for child properties (shown when the foldout is expanded).
            var content = new VisualElement();
            content.AddToClassList("unity-foldout__content");

            root.Add(header);
            root.Add(content);

            // Track the last typename so the content is only rebuilt when the assigned type changes,
            // not whenever a child field value changes.
            string lastTypename = property.managedReferenceFullTypename;

            void UpdateHeader (SerializedProperty p)
            {
                bool hasType = !string.IsNullOrEmpty(p.managedReferenceFullTypename);
                typeButton.text = GetTypeName(p).text;

#if UNITY_2021_3_OR_NEWER
                var attr = (SubclassSelectorAttribute)attribute;
                if (attr.UseToStringAsLabel && hasType && !p.hasMultipleDifferentValues)
                {
                    object managedValue = p.managedReferenceValue;
                    propLabel.text = (managedValue != null) ? managedValue.ToString() : property.displayName;
                }
                else
                {
                    propLabel.text = property.displayName;
                }
#endif

                expandToggle.style.visibility = hasType ? Visibility.Visible : Visibility.Hidden;
                expandToggle.SetValueWithoutNotify(hasType && p.isExpanded);
                content.style.display = (hasType && p.isExpanded) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            void RebuildContent (SerializedProperty p)
            {
                content.Clear();
                string typename = p.managedReferenceFullTypename;
                if (string.IsNullOrEmpty(typename))
                {
                    return;
                }

                Type propertyType = ManagedReferenceUtility.GetType(typename);
                if (propertyType != null && PropertyDrawerCache.TryGetPropertyDrawer(propertyType, out PropertyDrawer customDrawer))
                {
                    VisualElement customElement = customDrawer.CreatePropertyGUI(p.Copy());
                    if (customElement != null)
                    {
                        content.Add(customElement);
                        content.Bind(p.serializedObject);
                        return;
                    }

                    // The custom drawer has no UIToolkit support; fall back to IMGUI.
                    string propPath = p.propertyPath;
                    SerializedObject serializedObj = p.serializedObject;
                    content.Add(new IMGUIContainer(() =>
                    {
                        SerializedProperty currentProp = serializedObj.FindProperty(propPath);
                        if (currentProp == null) return;
                        float height = customDrawer.GetPropertyHeight(currentProp, GUIContent.none);
                        Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(height));
                        customDrawer.OnGUI(rect, currentProp, GUIContent.none);
                        serializedObj.ApplyModifiedProperties();
                    }));
                    return;
                }

                // Default: render each immediate child as a PropertyField.
                foreach (SerializedProperty child in p.GetChildProperties())
                {
                    content.Add(new PropertyField(child.Copy()));
                }
                content.Bind(p.serializedObject);
            }

            typeButton.clicked += () =>
            {
                TypePopupCache popupCache = GetTypePopup(property);
                targetProperty = property;
                popupCache.TypePopup.Show(typeButton.worldBound);
            };

            expandToggle.RegisterValueChangedCallback(evt =>
            {
                property.isExpanded = evt.newValue;
                property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                content.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
                if (evt.newValue && content.childCount == 0)
                {
                    RebuildContent(property);
                }
            });

            root.TrackPropertyValue(property, p =>
            {
                string currentTypename = p.managedReferenceFullTypename;
                bool typeChanged = currentTypename != lastTypename;
                lastTypename = currentTypename;

                UpdateHeader(p);

                if (typeChanged)
                {
                    if (p.isExpanded && !string.IsNullOrEmpty(currentTypename))
                    {
                        RebuildContent(p);
                    }
                    else
                    {
                        content.Clear();
                    }
                }
            });

            // Initial render.
            UpdateHeader(property);
            if (property.isExpanded && !string.IsNullOrEmpty(property.managedReferenceFullTypename))
            {
                RebuildContent(property);
            }

            return root;
        }

        public override void OnGUI (Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            if (property.propertyType == SerializedPropertyType.ManagedReference)
            {
                // Render label first to avoid label overlap for lists
                Rect foldoutLabelRect = new Rect(position);
                foldoutLabelRect.height = EditorGUIUtility.singleLineHeight;

                // NOTE: IndentedRect should be disabled as it causes extra indentation.
                //foldoutLabelRect = EditorGUI.IndentedRect(foldoutLabelRect);
                Rect popupPosition = EditorGUI.PrefixLabel(foldoutLabelRect, label);

#if UNITY_2021_3_OR_NEWER
                // Override the label text with the ToString() of the managed reference.
                var subclassSelectorAttribute = (SubclassSelectorAttribute)attribute;
                if (subclassSelectorAttribute.UseToStringAsLabel && !property.hasMultipleDifferentValues)
                {
                    object managedReferenceValue = property.managedReferenceValue;
                    if (managedReferenceValue != null)
                    {
                        label.text = managedReferenceValue.ToString();
                    }
                }
#endif

                // Draw the subclass selector popup.
                if (EditorGUI.DropdownButton(popupPosition, GetTypeName(property), FocusType.Keyboard))
                {
                    TypePopupCache popup = GetTypePopup(property);
                    targetProperty = property;
                    popup.TypePopup.Show(popupPosition);
                }

                // Draw the foldout.
                if (!string.IsNullOrEmpty(property.managedReferenceFullTypename))
                {
                    Rect foldoutRect = new Rect(position);
                    foldoutRect.height = EditorGUIUtility.singleLineHeight;

#if UNITY_2022_2_OR_NEWER && !UNITY_6000_0_OR_NEWER && !UNITY_2022_3
                    // NOTE: Position x must be adjusted in certain Unity versions (IMGUI only).
                    // 2021.3: No adjustment
                    // 2022.1: No adjustment
                    // 2022.2: Adjustment required
                    // 2022.3: Adjustment required
                    // 2023.1: Adjustment required
                    // 2023.2: Adjustment required
                    // 6000.0: No adjustment
                    foldoutRect.x -= 12;
#endif

                    property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, GUIContent.none, true);
                }

                // Draw property if expanded.
                if (property.isExpanded)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        // Check if a custom property drawer exists for this type.
                        PropertyDrawer customDrawer = GetCustomPropertyDrawer(property);
                        if (customDrawer != null)
                        {
                            // Draw the property with custom property drawer.
                            Rect indentedRect = position;
                            float foldoutDifference = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                            indentedRect.height = customDrawer.GetPropertyHeight(property, label);
                            indentedRect.y += foldoutDifference;
                            customDrawer.OnGUI(indentedRect, property, label);
                        }
                        else
                        {
                            // Draw the properties of the child elements.
                            // NOTE: In the following code, since the foldout layout isn't working properly, I'll iterate through the properties of the child elements myself.
                            // EditorGUI.PropertyField(position, property, GUIContent.none, true);

                            Rect childPosition = position;
                            childPosition.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
                            foreach (SerializedProperty childProperty in property.GetChildProperties())
                            {
                                float height = EditorGUI.GetPropertyHeight(childProperty, new GUIContent(childProperty.displayName, childProperty.tooltip), true);
                                childPosition.height = height;
                                EditorGUI.PropertyField(childPosition, childProperty, true);

                                childPosition.y += height + EditorGUIUtility.standardVerticalSpacing;
                            }
                        }
                    }
                }
            }
            else
            {
                EditorGUI.LabelField(position, label, IsNotManagedReferenceLabel);
            }

            EditorGUI.EndProperty();
        }

        private PropertyDrawer GetCustomPropertyDrawer (SerializedProperty property)
        {
            Type propertyType = ManagedReferenceUtility.GetType(property.managedReferenceFullTypename);
            if (propertyType != null && PropertyDrawerCache.TryGetPropertyDrawer(propertyType, out PropertyDrawer drawer))
            {
                return drawer;
            }
            return null;
        }

        private TypePopupCache GetTypePopup (SerializedProperty property)
        {
            // Cache this string. This property internally call Assembly.GetName, which result in a large allocation.
            string managedReferenceFieldTypename = property.managedReferenceFieldTypename;

            if (!typePopups.TryGetValue(managedReferenceFieldTypename, out TypePopupCache result))
            {
                var state = new AdvancedDropdownState();

                Type baseType = ManagedReferenceUtility.GetType(managedReferenceFieldTypename);
                var types = TypeSearchService.TypeCandiateService.GetDisplayableTypes(baseType);
                var popup = new AdvancedTypePopup(
                    types,
                    MaxTypePopupLineCount,
                    state
                );
                popup.OnItemSelected += item =>
                {
                    Type type = item.Type;

                    // Apply changes to individual serialized objects.
                    foreach (var targetObject in targetProperty.serializedObject.targetObjects)
                    {
                        SerializedObject individualObject = new SerializedObject(targetObject);
                        SerializedProperty individualProperty = individualObject.FindProperty(targetProperty.propertyPath);
                        object obj = individualProperty.SetManagedReference(type);
                        individualProperty.isExpanded = (obj != null);

                        individualObject.ApplyModifiedProperties();
                        individualObject.Update();
                    }
                };

                result = new TypePopupCache(popup, state);
                typePopups.Add(managedReferenceFieldTypename, result);
            }
            return result;
        }

        private GUIContent GetTypeName (SerializedProperty property)
        {
            // Cache this string.
            string managedReferenceFullTypename = property.managedReferenceFullTypename;

            if (string.IsNullOrEmpty(managedReferenceFullTypename))
            {
                return NullDisplayName;
            }
            if (typeNameCaches.TryGetValue(managedReferenceFullTypename, out GUIContent cachedTypeName))
            {
                return cachedTypeName;
            }

            Type type = ManagedReferenceUtility.GetType(managedReferenceFullTypename);
            string typeName = null;

            AddTypeMenuAttribute typeMenu = TypeMenuUtility.GetAttribute(type);
            if (typeMenu != null)
            {
                typeName = typeMenu.GetTypeNameWithoutPath();
                if (!string.IsNullOrWhiteSpace(typeName))
                {
                    typeName = ObjectNames.NicifyVariableName(typeName);
                }
            }

            if (string.IsNullOrWhiteSpace(typeName))
            {
                typeName = ObjectNames.NicifyVariableName(type.Name);
            }

            GUIContent result = new GUIContent(typeName);
            typeNameCaches.Add(managedReferenceFullTypename, result);
            return result;
        }

        public override float GetPropertyHeight (SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.ManagedReference)
            {
                return EditorGUIUtility.singleLineHeight;
            }
            if (!property.isExpanded || string.IsNullOrEmpty(property.managedReferenceFullTypename))
            {
                return EditorGUIUtility.singleLineHeight;
            }

            float height = EditorGUIUtility.singleLineHeight;
            height += EditorGUIUtility.standardVerticalSpacing;

            PropertyDrawer customDrawer = GetCustomPropertyDrawer(property);
            if (customDrawer != null)
            {
                height += customDrawer.GetPropertyHeight(property, label);
                return height;
            }

            height += GetChildrenHeight(property);

            return height;
        }

        private static float GetChildrenHeight (SerializedProperty property)
        {
            float height = 0f;
            bool first = true;

            foreach (SerializedProperty child in property.GetChildProperties())
            {
                if (!first)
                {
                    height += EditorGUIUtility.standardVerticalSpacing;
                }
                first = false;

                TempChildLabel.text = child.displayName;
                TempChildLabel.tooltip = child.tooltip;

                height += EditorGUI.GetPropertyHeight(child, TempChildLabel, true);
            }

            return height;
        }

    }
}
