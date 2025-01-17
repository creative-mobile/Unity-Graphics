using System.Collections.Generic;
using System.IO;
using UnityEditor.Rendering.Analytics;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEditor.Rendering
{
    [CustomEditor(typeof(Volume))]
    sealed class VolumeEditor : Editor
    {
        static class Styles
        {
            public static readonly GUIContent mode = EditorGUIUtility.TrTextContent("Mode", "Global Volumes affect the Camera wherever the Camera is in the Scene and Local Volumes affect the Camera if they encapsulate the Camera within the bounds of their Collider.");
            public static readonly GUIContent[] modes = { EditorGUIUtility.TrTextContent("Global"), EditorGUIUtility.TrTextContent("Local") };

            public static readonly GUIContent addBoxCollider = EditorGUIUtility.TrTextContent("Add a Box Collider");
            public static readonly GUIContent sphereBoxCollider = EditorGUIUtility.TrTextContent("Add a Sphere Collider");
            public static readonly GUIContent capsuleBoxCollider = EditorGUIUtility.TrTextContent("Add a Capsule Collider");
            public static readonly GUIContent meshBoxCollider = EditorGUIUtility.TrTextContent("Add a Mesh Collider");

            public static readonly GUIContent addColliderFixMessage = EditorGUIUtility.TrTextContentWithIcon("Add a Collider to this GameObject to set boundaries for the local Volume.", CoreEditorStyles.iconWarn);
            public static readonly GUIContent disableColliderFixMessage = EditorGUIUtility.TrTextContentWithIcon("Global Volumes do not need a collider. Disable or remove the collider.", CoreEditorStyles.iconWarn);
            public static readonly GUIContent enableColliderFixMessage = EditorGUIUtility.TrTextContentWithIcon("Local Volumes need a collider enabled. Enable the collider.", CoreEditorStyles.iconWarn);

            public static readonly GUIContent profileInstance = EditorGUIUtility.TrTextContent("Profile (Instance)", "A Volume Profile is a Scriptable Object which contains properties that Volumes use to determine how to render the Scene environment for Cameras they affect.");
            public static readonly GUIContent profile = EditorGUIUtility.TrTextContent("Profile", "A Volume Profile is a Scriptable Object which contains properties that Volumes use to determine how to render the Scene environment for Cameras they affect.");

            public static readonly GUIContent newLabel = EditorGUIUtility.TrTextContent("New", "Create a new profile.");
            public static readonly GUIContent saveLabel = EditorGUIUtility.TrTextContent("Save", "Save the instantiated profile");
            public static readonly GUIContent cloneLabel = EditorGUIUtility.TrTextContent("Clone", "Create a new profile and copy the content of the currently assigned profile.");
            public static readonly string noVolumeMessage = L10n.Tr("Please select or create a new Volume profile to begin applying effects to the scene.");
        }

        SerializedProperty m_IsGlobal;
        SerializedProperty m_BlendRadius;
        SerializedProperty m_Weight;
        SerializedProperty m_Priority;
        SerializedProperty m_Profile;

        VolumeComponentListEditor m_ComponentList;

        Volume actualTarget => target as Volume;

        VolumeProfile profileRef => actualTarget.HasInstantiatedProfile() ? actualTarget.profile : actualTarget.sharedProfile;

        void OnEnable()
        {
            var o = new PropertyFetcher<Volume>(serializedObject);
            m_IsGlobal = o.Find(x => x.isGlobal);
            m_BlendRadius = o.Find(x => x.blendDistance);
            m_Weight = o.Find(x => x.weight);
            m_Priority = o.Find(x => x.priority);
            m_Profile = o.Find(x => x.sharedProfile);

            m_ComponentList = new VolumeComponentListEditor(this);
            RefreshEffectListEditor(actualTarget.sharedProfile);
        }

        void OnDisable()
        {
            m_ComponentList?.Clear();
        }

        void RefreshEffectListEditor(VolumeProfile asset)
        {
            m_ComponentList.Clear();

            if (asset != null)
            {
                asset.Sanitize();
                m_ComponentList.Init(asset, new SerializedObject(asset));
            }
        }

        private void AddOverride()
        {
            var menu = new GenericMenu();
            menu.AddItem(Styles.addBoxCollider, false, () => Undo.AddComponent<BoxCollider>(actualTarget.gameObject));
            menu.AddItem(Styles.sphereBoxCollider, false, () => Undo.AddComponent<SphereCollider>(actualTarget.gameObject));
            menu.AddItem(Styles.capsuleBoxCollider, false, () => Undo.AddComponent<CapsuleCollider>(actualTarget.gameObject));
            menu.AddItem(Styles.meshBoxCollider, false, () => Undo.AddComponent<MeshCollider>(actualTarget.gameObject));
            menu.ShowAsContext();
        }

        [MenuItem("CONTEXT/Volume/Remove All Overrides")]
        private static void RemoveAllOverrides(MenuCommand command)
        {
            if (command.context is Volume volumeComponent)
            {
                volumeComponent.profile.components.Clear();
            }
        }

        private static void SetVolumeOverridesActiveState(List<VolumeComponent> components, bool activeState)
        {
            foreach (var v in components)
            {
                v.active = activeState;
            }
        }

        [MenuItem("CONTEXT/Volume/Disable All Overrides")]
        private static void DisableAllOverrides(MenuCommand command)
        {
            if (command.context is Volume volumeComponent)
                SetVolumeOverridesActiveState(volumeComponent.profile.components, false);
        }

        [MenuItem("CONTEXT/Volume/Enable All Overrides")]
        private static void EnableAllOverrides(MenuCommand command)
        {
            if (command.context is Volume volumeComponent)
                SetVolumeOverridesActiveState(volumeComponent.profile.components, true);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            Rect lineRect = EditorGUILayout.GetControlRect();
            int isGlobal = m_IsGlobal.boolValue ? 0 : 1;
            using (new EditorGUI.PropertyScope(lineRect, Styles.mode, m_IsGlobal))
            {
                EditorGUI.BeginChangeCheck();
                isGlobal = EditorGUI.Popup(lineRect, Styles.mode, isGlobal, Styles.modes);
                if (EditorGUI.EndChangeCheck())
                    m_IsGlobal.boolValue = isGlobal == 0;
            }

            if (isGlobal != 0) // Blend radius is not needed for global volumes
            {
                if (actualTarget.TryGetComponent<Collider>(out var collider))
                {
                    if (!collider.enabled)
                        CoreEditorUtils.DrawFixMeBox(Styles.enableColliderFixMessage, () => collider.enabled = true);
                }
                else
                {
                    CoreEditorUtils.DrawFixMeBox(Styles.addColliderFixMessage, AddOverride);
                }

                using var indentScope = new EditorGUI.IndentLevelScope();

                EditorGUILayout.PropertyField(m_BlendRadius);
                m_BlendRadius.floatValue = Mathf.Max(m_BlendRadius.floatValue, 0f);
            }
            else
            {
                if (actualTarget.TryGetComponent<Collider>(out var collider))
                {
                    if (collider.enabled)
                        CoreEditorUtils.DrawFixMeBox(Styles.disableColliderFixMessage, () => collider.enabled = false);
                }
            }

            EditorGUILayout.PropertyField(m_Weight);

            bool priorityHasChanged = false;
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                EditorGUILayout.PropertyField(m_Priority);
                priorityHasChanged = scope.changed;
            }

            bool assetHasChanged = false;
            bool showCopy = m_Profile.objectReferenceValue != null;
            bool multiEdit = m_Profile.hasMultipleDifferentValues;

            // The layout system breaks alignment when mixing inspector fields with custom layout'd
            // fields, do the layout manually instead
            int buttonWidth = showCopy ? 45 : 60;
            float indentOffset = EditorGUI.indentLevel * 15f;
            lineRect = EditorGUILayout.GetControlRect();
            var labelRect = new Rect(lineRect.x, lineRect.y, EditorGUIUtility.labelWidth - indentOffset - 3, lineRect.height);
            var fieldRect = new Rect(labelRect.xMax + 5, lineRect.y, lineRect.width - labelRect.width - buttonWidth * (showCopy ? 2 : 1) - 5, lineRect.height);
            var buttonNewRect = new Rect(fieldRect.xMax, lineRect.y, buttonWidth, lineRect.height);
            var buttonCopyRect = new Rect(buttonNewRect.xMax, lineRect.y, buttonWidth, lineRect.height);

            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var isProfileInstance = actualTarget.HasInstantiatedProfile();
                VolumeProfile editedProfile;
                if (isProfileInstance)
                {
                    using var mixed = new EditorGUI.MixedValueScope(m_Profile.hasMultipleDifferentValues);
                    EditorGUI.PrefixLabel(labelRect, Styles.profileInstance);
                    editedProfile = (VolumeProfile)EditorGUI.ObjectField(fieldRect, actualTarget.profile, typeof(VolumeProfile), false);
                }
                else
                {
                    fieldRect = new Rect(labelRect.x, labelRect.y, labelRect.width + fieldRect.width, fieldRect.height);
                    EditorGUI.ObjectField(fieldRect, m_Profile, Styles.profile);
                    editedProfile = (VolumeProfile)m_Profile.objectReferenceValue;
                }

                if (scope.changed)
                {
                    assetHasChanged = true;
                    if (isProfileInstance)
                        // Clear the instantiated profile, from now on we're using shared again
                        actualTarget.profile = null;
                    else
                        m_Profile.objectReferenceValue = editedProfile;
                }
            }

            using (new EditorGUI.DisabledScope(multiEdit))
            {
                if (GUI.Button(buttonNewRect, Styles.newLabel, showCopy ? EditorStyles.miniButtonLeft : EditorStyles.miniButton))
                {
                    // By default, try to put assets in a folder next to the currently active
                    // scene file. If the user isn't a scene, put them in root instead.
                    var targetName = actualTarget.name + " Profile";
                    var scene = actualTarget.gameObject.scene;
                    var asset = VolumeProfileFactory.CreateVolumeProfile(scene, targetName);
                    m_Profile.objectReferenceValue = asset;
                    actualTarget.profile = null; // Make sure we're not using an instantiated profile anymore
                    assetHasChanged = true;
                }

                GUIContent guiContent = actualTarget.HasInstantiatedProfile() ? Styles.saveLabel : Styles.cloneLabel;
                if (showCopy && GUI.Button(buttonCopyRect, guiContent, EditorStyles.miniButtonRight))
                {
                    // Duplicate the currently assigned profile and save it as a new profile
                    var origin = profileRef;
                    var path = AssetDatabase.GetAssetPath(m_Profile.objectReferenceValue);

                    path = CoreEditorUtils.IsAssetInReadOnlyPackage(path)

                        // We may be in a read only package, in that case we need to clone the volume profile in an
                        // editable area, such as the root of the project.
                        ? AssetDatabase.GenerateUniqueAssetPath(Path.Combine("Assets", Path.GetFileName(path)))

                        // Otherwise, duplicate next to original asset.
                        : AssetDatabase.GenerateUniqueAssetPath(path);

                    var asset = Instantiate(origin);
                    asset.components.Clear();
                    AssetDatabase.CreateAsset(asset, path);

                    foreach (var item in origin.components)
                    {
                        var itemCopy = Instantiate(item);
                        itemCopy.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                        itemCopy.name = item.name;
                        asset.components.Add(itemCopy);
                        AssetDatabase.AddObjectToAsset(itemCopy, asset);
                    }

                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();

                    m_Profile.objectReferenceValue = asset;
                    actualTarget.profile = null; // Make sure we're not using an instantiated profile anymore
                    assetHasChanged = true;
                }
            }

            EditorGUILayout.Space();

            if (m_Profile.objectReferenceValue == null && !actualTarget.HasInstantiatedProfile())
            {
                if (assetHasChanged)
                    m_ComponentList.Clear(); // Asset wasn't null before, do some cleanup
            }
            else
            {
                if (assetHasChanged || profileRef != m_ComponentList.asset)
                {
                    serializedObject.ApplyModifiedProperties();
                    serializedObject.Update();
                    RefreshEffectListEditor(profileRef);
                }

                if (!multiEdit)
                {
                    m_ComponentList.OnGUI();
                    EditorGUILayout.Space();
                }
            }

            if (actualTarget.sharedProfile == null && m_Profile.objectReferenceValue != null)
            {
                if (Event.current.type != EventType.Layout)
                {
                    actualTarget.sharedProfile = (VolumeProfile)m_Profile.objectReferenceValue;
                    if (actualTarget.HasInstantiatedProfile())
                        actualTarget.profile = null;
                    RefreshEffectListEditor(actualTarget.sharedProfile);
                }
            }

            serializedObject.ApplyModifiedProperties();

            if (assetHasChanged)
                VolumeProfileUsageAnalytic.Send(actualTarget, (VolumeProfile)m_Profile.objectReferenceValue);

            if (priorityHasChanged)
                VolumePriorityUsageAnalytic.Send(actualTarget);

            if (m_Profile.objectReferenceValue == null && actualTarget.HasInstantiatedProfile())
                EditorGUILayout.HelpBox(Styles.noVolumeMessage, MessageType.Info);
        }
    }
}
