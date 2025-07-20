#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CustomAttributes;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReverseTowerDefense
{
    [CustomEditor(typeof(MonoBehaviour), true)]
    public class MonoBehaviorEditor : Editor
    {
        public struct ListInfo
        {
            public IList ActualList;
            public bool ShowList;
            public string ListElementName;
            public bool IsCustomDataTypeList;

            public ListInfo(IList actualList, bool showList, string listElementName, bool isCustomDataTypeList)
            {
                ActualList = actualList;
                ShowList = showList;
                ListElementName = listElementName;
                IsCustomDataTypeList = isCustomDataTypeList;
            }
        }

        public struct FoldoutInfo
        {
            public int StartIndex;
            public int EndIndex;
            public string Label;

            public FoldoutInfo(int startIndex, int endIndex, string label)
            {
                StartIndex = startIndex;
                EndIndex = endIndex;
                Label = label;
            }
        }

        private static GUIStyle centeredBoldLabelStyle;
        private static GUIStyle boldLabelStyle;
        private static GUIStyle boldFoldoutStyle;

        // Contains the data of non-serialized as well as serialized lists
        private Dictionary<FieldInfo, ListInfo> serializedLists;
        private Dictionary<string, ReorderableList> nameToReorderableListsMap;
        private Dictionary<FieldInfo, bool> customTypeFieldToFoldoutMap = new Dictionary<FieldInfo, bool>();
        private Dictionary<string, bool> listElementFoldoutsMap = new Dictionary<string, bool>();

        #region Debug Mode Handlers

        private bool isPreviewingAnything = false;

        private readonly string showPrivateStuffWithInstanceID = "Show Private Stuff_{0}";
        private Dictionary<FieldInfo, ListInfo> privateFieldToListMap = new Dictionary<FieldInfo, ListInfo>();
        private List<FieldInfo> privateFieldsList;
        private bool showPrivateFields = false;

        private EditorDebugModeConfigSO editorDebugModeConfigSO;

        private void PopulateFields(BindingFlags bindingFlags, ref List<FieldInfo> fieldsList)
        {
            fieldsList = new List<FieldInfo>();

            Type type = target.GetType();

            foreach (FieldInfo field in type.GetFields(bindingFlags))
            {
                if ((bindingFlags & BindingFlags.Public) == 0 && field.GetCustomAttribute<SerializeField>() == null)
                {
                    fieldsList.Add(field);

                    if (IsFieldStructType(field.FieldType) || IsFieldCustomClassType(field.FieldType, field.GetValue(target)))
                    {
                        customTypeFieldToFoldoutMap.Add(field, false);
                    }
                }
            }
        }

        private object DrawCustomTypeInline(string fieldName, object value, FieldInfo[] customDataTypeFields, bool isEditable)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(fieldName, GUILayout.Width(EditorGUIUtility.labelWidth));

            foreach (FieldInfo field in customDataTypeFields)
            {
                DrawCustomClassField(ObjectNames.NicifyVariableName(field.Name), field.FieldType, value, field, isEditable);
            }

            EditorGUILayout.EndHorizontal();

            return value;
        }

        private object DrawCustomClassField(string fieldName, Type fieldType, object value, FieldInfo fieldInfo, bool isEditable)
        {
            var style = new GUIStyle(GUI.skin.label);

            Vector2 size = style.CalcSize(new GUIContent(fieldName));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(fieldName, GUILayout.Width(size.x + EditorGUI.indentLevel * 15f));

            DrawFieldInEditor("", fieldType, fieldInfo, fieldInfo.GetValue(value), GUILayout.MinWidth(GetWidthBasedOnTypeOfField(fieldType)), out object newValue);

            if (isEditable)
            {
                fieldInfo.SetValue(value, newValue);
            }

            EditorGUILayout.EndHorizontal();

            return value;
        }

        private object DrawCustomTypeWithFoldout(string fieldName, Type fieldType, object value, FieldInfo[] customDataTypeFields, bool isEditable, ref bool customTypeFoldout)
        {
            if (isEditable)
            {
                customTypeFoldout = EditorGUILayout.Foldout(customTypeFoldout, fieldName);
            }
            else
            {
                GUI.enabled = true;
                customTypeFoldout = EditorGUILayout.Foldout(customTypeFoldout, fieldName);
                GUI.enabled = false;
            }

            if (customTypeFoldout)
            {
                EditorGUI.indentLevel++;

                foreach (FieldInfo customClassField in customDataTypeFields)
                {
                    DrawFieldInEditor(ObjectNames.NicifyVariableName(customClassField.Name), customClassField.FieldType, customClassField, customClassField.GetValue(value), GUILayout.MaxWidth(1000f), out object newValue);

                    if(isEditable)
                    {
                        customClassField.SetValue(value, newValue);
                    }
                }

                EditorGUI.indentLevel--;
            }

            return value;
        }

        private void DrawFields()
        {
            foreach (FieldInfo readonlyField in privateFieldsList)
            {
                string fieldName = ObjectNames.NicifyVariableName(readonlyField.Name);

                if (privateFieldToListMap.ContainsKey(readonlyField))
                {
                    ListInfo listInfo = privateFieldToListMap[readonlyField];

                    DrawListInEditor(fieldName, readonlyField, readonlyField.FieldType, isEditable: false, ref listInfo, out object newValue);
                    privateFieldToListMap[readonlyField] = listInfo;

                    continue;
                }

                object value = readonlyField.GetValue(target);
                Type fieldType = readonlyField.FieldType;

                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    IList list = value as IList;

                    ListElementAttribute listElementAttribute = readonlyField.GetCustomAttribute<ListElementAttribute>();
                    string elementName = "Element";

                    if(listElementAttribute != null)
                    {
                        elementName = listElementAttribute.ElementName;
                    }

                    Type listElementType = fieldType.GetGenericArguments()[0];
                    bool isCustomDataTypeList = IsFieldCustomClassType(fieldType.GetGenericArguments()[0], value) || IsFieldStructType(listElementType);

                    ListInfo newListInfo = new ListInfo(list, EditorPrefs.GetBool(ObjectNames.NicifyVariableName(readonlyField.Name), false), elementName, isCustomDataTypeList);

                    privateFieldToListMap.Add(readonlyField, newListInfo);
                }

                GUI.enabled = false;

                DrawFieldInEditor(fieldName, fieldType, readonlyField, value, GUILayout.MaxWidth(1000f), out object _);

                GUI.enabled = true;
            }
        }

        private void HandlePrivateFields()
        {
            if (privateFieldsList.Count > 0)
            {
                GUILayout.Space(5f);

                if (showPrivateFields)
                {
                    DrawFields();
                }

                SaveListFoldoutPrefs();
            }
        }

        private void SaveListFoldoutPrefs()
        {
            foreach(var pair in privateFieldToListMap)
            {
                EditorPrefs.SetBool(ObjectNames.NicifyVariableName(pair.Key.Name), pair.Value.ShowList);
            }
        }

        private void HandleDebugButtons()
        {
            GUILayout.Space(10f);
            if (isPreviewingAnything)
            {
                if (GUILayout.Button("Normal Mode"))
                {
                    isPreviewingAnything = false;
                    showPrivateFields = false;
                }
            }
            else if (!isPreviewingAnything)
            {
                if (GUILayout.Button("Debug Mode"))
                {
                    showPrivateFields = true;
                    isPreviewingAnything = true;
                }
            }
        }

        private void HandleDebugMode()
        {
            string privateValue = string.Format(showPrivateStuffWithInstanceID, serializedObject.targetObject.GetInstanceID());
            showPrivateFields = EditorPrefs.GetBool(privateValue, showPrivateFields);

            isPreviewingAnything = showPrivateFields;

            if (privateFieldsList == null)
            {
                PopulateFields(BindingFlags.Instance | BindingFlags.NonPublic, ref privateFieldsList);
            }

            HandleDebugButtons();

            HandlePrivateFields();

            EditorPrefs.SetBool(privateValue, showPrivateFields);
        }

        #endregion

        #region Button Handlers

        private List<MethodInfo> buttonMethodsList;
        private Dictionary<string, object[]> methodParametersMap = new Dictionary<string, object[]>();
        private Dictionary<MethodInfo, bool> methodToClickingInfoMap = new Dictionary<MethodInfo, bool>();

        private void PopulateButtonMethods()
        {
            buttonMethodsList = new List<MethodInfo>();
            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;

            foreach (MethodInfo method in type.GetMethods(flags))
            {
                if (method.GetCustomAttribute<ButtonAttribute>() != null)
                {
                    buttonMethodsList.Add(method);
                    methodToClickingInfoMap.Add(method, false);
                }
            }
        }

        private void DrawButtons()
        {
            foreach (MethodInfo buttonMethod in buttonMethodsList)
            {
                GUILayout.Space(10f);
                bool clickingButton = methodToClickingInfoMap[buttonMethod];

                if (clickingButton)
                {
                    EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(buttonMethod.Name), centeredBoldLabelStyle);
                    ParameterInfo[] parameterInfos = buttonMethod.GetParameters();

                    if (!methodParametersMap.ContainsKey(buttonMethod.Name))
                    {
                        methodParametersMap.Add(buttonMethod.Name, new object[parameterInfos.Length]);
                    }

                    object[] parametersCache = methodParametersMap[buttonMethod.Name];

                    for (int i = 0; i < parameterInfos.Length; i++)
                    {
                        ParameterInfo parameterInfo = parameterInfos[i];
                        DrawMethodParameter(parameterInfo.ParameterType, ObjectNames.NicifyVariableName(parameterInfo.Name), ref parametersCache[i]);
                    }

                    EditorGUILayout.BeginHorizontal();

                    if (GUILayout.Button("Cancel"))
                    {
                        clickingButton = false;
                    }

                    if (GUILayout.Button("Invoke"))
                    {
                        buttonMethod.Invoke(target, parametersCache);
                    }

                    EditorGUILayout.EndHorizontal();
                }
                else if (GUILayout.Button(ObjectNames.NicifyVariableName(buttonMethod.Name)))
                {
                    clickingButton = true;
                }

                methodToClickingInfoMap[buttonMethod] = clickingButton;
            }
        }

        private void HandleButtonMethods()
        {
            if (buttonMethodsList == null)
            {
                PopulateButtonMethods();
            }

            DrawButtons();
        }

        private void DrawMethodParameter(Type fieldType, string fieldName, ref object value)
        {
            if (fieldType == typeof(string))
            {
                value = EditorGUILayout.TextField(fieldName, value is string val ? val : "");
            }
            else if (fieldType == typeof(bool))
            {
                value = EditorGUILayout.Toggle(fieldName, value is bool val && val);
            }
            else if (fieldType == typeof(int))
            {
                value = EditorGUILayout.IntField(fieldName, value is int val ? val : 0);
            }
            else if (fieldType == typeof(float))
            {
                value = EditorGUILayout.FloatField(fieldName, value is float val ? val : 0f);
            }
            else if (typeof(Object).IsAssignableFrom(fieldType))
            {
                value = EditorGUILayout.ObjectField(fieldName, (Object)value, fieldType, allowSceneObjects: false);
            }
            else if (fieldType.IsEnum)
            {
                value = EditorGUILayout.EnumPopup(fieldName, (Enum)value);
            }
            else if (IsFieldStructType(fieldType) && fieldType == typeof(Vector3))
            {
                value = EditorGUILayout.Vector3Field(fieldName, value is Vector3 val ? val : Vector3.zero);
            }
            else if (IsFieldStructType(fieldType) || IsFieldCustomClassType(fieldType, value))
            {
                DrawMethodParameterOfCustomType(fieldType, fieldName, ref value);
            }
        }

        private void DrawMethodParameterOfCustomType(Type fieldType, string fieldName, ref object value)
        {
            //BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;

            ////GUILayout.FlexibleSpace();
            //EditorGUILayout.BeginHorizontal();

            //EditorGUILayout.LabelField(fieldName);

            //foreach (FieldInfo customClassField in fieldType.GetFields(flags))
            //{
            //    DrawMethodParameter(customClassField.FieldType, ObjectNames.NicifyVariableName(customClassField.Name), ref value);
            //}

            ////GUILayout.FlexibleSpace();
            //EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region Foldouts Handler Functions

        private Dictionary<FoldoutInfo, bool> foldoutInfoMap;
        private List<FieldInfo> serializeFields;

        private void HandleFoldouts()
        {
            if (foldoutInfoMap == null || serializeFields == null)
            {
                PopulateFoldouts();
            }

            DrawFoldouts();

            SaveEditorPrefsForFoldouts();
            SaveEditorPrefsForSerializedLists();
        }

        private void SaveEditorPrefsForFoldouts()
        {
            foreach (var foldoutInfo in foldoutInfoMap)
            {
                EditorPrefs.SetBool($"{foldoutInfo.Key.Label} {ObjectNames.NicifyVariableName(target.name)}", foldoutInfo.Value);
            }
        }

        private void SaveEditorPrefsForSerializedLists()
        {
            foreach (var pair in serializedLists)
            {
                EditorPrefs.SetBool(ObjectNames.NicifyVariableName(pair.Key.Name), pair.Value.ShowList);
            }
        }

        private void DrawFoldouts()
        {
            if (foldoutInfoMap.Count == 0) return;

            int currentFoldoutIndex = 0;
            FoldoutInfo currentFoldoutInfo = foldoutInfoMap.First().Key;

            int foldoutStartIndex = currentFoldoutInfo.StartIndex;
            int foldoutEndIndex = currentFoldoutInfo.EndIndex;
            string foldoutLabel = currentFoldoutInfo.Label;

            int i = 0;

            while (i < serializeFields.Count)
            {
                FieldInfo fieldInfo = serializeFields[i];

                if (i == foldoutStartIndex)
                {
                    bool foldout = EditorGUILayout.Foldout(foldoutInfoMap[currentFoldoutInfo], foldoutLabel, toggleOnLabelClick: true, boldFoldoutStyle);

                    if (foldout)
                    {
                        EditorGUI.indentLevel++;

                        while (i >= foldoutStartIndex && i <= foldoutEndIndex)
                        {
                            fieldInfo = serializeFields[i];
                            DrawSerializedField(fieldInfo);

                            i += 1;
                        }

                        EditorGUI.indentLevel--;
                    }

                    currentFoldoutIndex += 1;
                    foldoutInfoMap[currentFoldoutInfo] = foldout;

                    currentFoldoutInfo = GetCurrentFoldoutInfoFromMap(currentFoldoutIndex);
                    i = foldoutEndIndex + 1;

                    foldoutStartIndex = currentFoldoutInfo.StartIndex;
                    foldoutEndIndex = currentFoldoutInfo.EndIndex;
                    foldoutLabel = currentFoldoutInfo.Label;
                }
                else
                {
                    i += 1;
                }
            }
        }

        private void DrawSerializedField(FieldInfo fieldInfo)
        {
            HandleFieldAsShowIfField(fieldInfo, out bool canDraw);

            if (canDraw)
            {
                DrawFieldInEditor(ObjectNames.NicifyVariableName(fieldInfo.Name), fieldInfo.FieldType, fieldInfo, fieldInfo.GetValue(target), GUILayout.MaxWidth(1000f), out object newValue);

                fieldInfo.SetValue(target, newValue);
            }
        }

        private FoldoutInfo GetCurrentFoldoutInfoFromMap(int currentFoldoutIndex)
        {
            int currentIteration = 0;
            foreach (var mapPair in foldoutInfoMap)
            {
                if (currentFoldoutIndex == currentIteration)
                {
                    return mapPair.Key;
                }
                currentIteration += 1;
            }

            return new FoldoutInfo();
        }

        private void PopulateFoldouts()
        {
            serializedLists = new Dictionary<FieldInfo, ListInfo>();
            nameToReorderableListsMap = new Dictionary<string, ReorderableList>();

            foldoutInfoMap = new Dictionary<FoldoutInfo, bool>();
            serializeFields = new List<FieldInfo>();

            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            Type type = target.GetType();
            FieldInfo[] fields = type.GetFields(flags);
            serializeFields = fields.Where(field => field.GetCustomAttribute<SerializeField>() != null).ToList();

            int currentFoldoutStartIndex = 0;
            string currentFoldoutLabel = "Serialized Fields";

            for (int i = 0; i < serializeFields.Count; i++)
            {
                FieldInfo field = serializeFields[i];
                Type fieldType = field.FieldType;
                object value = field.GetValue(target);

                if (field.GetCustomAttribute<BeginFoldoutAttribute>() != null)
                {
                    if (i != 0)
                    {
                        foldoutInfoMap.Add(new FoldoutInfo(currentFoldoutStartIndex, i - 1, currentFoldoutLabel), EditorPrefs.GetBool($"{currentFoldoutLabel} {ObjectNames.NicifyVariableName(target.name)}", false));
                    }

                    currentFoldoutLabel = field.GetCustomAttribute<BeginFoldoutAttribute>().FoldoutName;
                    currentFoldoutStartIndex = i;
                }

                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    ListElementAttribute listElementAttribute = field.GetCustomAttribute<ListElementAttribute>();
                    string elementName = "Element";

                    if (listElementAttribute != null)
                    {
                        elementName = listElementAttribute.ElementName;
                    }

                    Type listElementType = fieldType.GetGenericArguments()[0];
                    bool isCustomDataTypeList = IsFieldCustomClassType(fieldType.GetGenericArguments()[0], value) || IsFieldStructType(listElementType);

                    serializedLists.Add(field, new ListInfo(field.GetValue(target) as IList, EditorPrefs.GetBool(ObjectNames.NicifyVariableName(field.Name), false), elementName, isCustomDataTypeList));
                }

                if (IsFieldStructType(field.FieldType) || IsFieldCustomClassType(field.FieldType, field.GetValue(target)))
                {
                    customTypeFieldToFoldoutMap.Add(field, false);
                }
            }

            foldoutInfoMap.Add(new FoldoutInfo(currentFoldoutStartIndex, serializeFields.Count - 1, currentFoldoutLabel), EditorPrefs.GetBool($"{currentFoldoutLabel} {ObjectNames.NicifyVariableName(target.name)}", false));
        }

        #endregion

        #region Helper Functions

        private void DrawFieldInEditor(string fieldName, Type fieldType, FieldInfo fieldInfo, object value, GUILayoutOption guiLayoutOption, out object newValue)
        {
            newValue = default;
            if (fieldType == typeof(string))
            {
                newValue = EditorGUILayout.TextField(fieldName, (string)value, guiLayoutOption);
            }
            else if (fieldType == typeof(bool))
            {
                newValue = EditorGUILayout.Toggle(fieldName, (bool)value, guiLayoutOption);
            }
            else if (fieldType == typeof(int))
            {
                newValue = EditorGUILayout.IntField(fieldName, (int)value, guiLayoutOption);
            }
            else if (fieldType == typeof(float))
            {
                newValue = EditorGUILayout.FloatField(fieldName, (float)value, guiLayoutOption);
            }
            else if (typeof(Object).IsAssignableFrom(fieldType))
            {
                newValue = EditorGUILayout.ObjectField(fieldName, (Object)value, fieldType, allowSceneObjects: false, guiLayoutOption);
            }
            else if (fieldType.IsEnum)
            {
                newValue = EditorGUILayout.EnumPopup(fieldName, (Enum)value, guiLayoutOption);
            }
            else if(fieldType == typeof(AnimationCurve))
            {
                newValue = EditorGUILayout.CurveField(fieldName, (AnimationCurve)value, guiLayoutOption);
            }
            else if(fieldType == typeof(Vector3))
            {
                newValue = EditorGUILayout.Vector3Field(fieldName, (Vector3)value, guiLayoutOption);
            }
            else if (fieldType == typeof(Vector2))
            {
                newValue = EditorGUILayout.Vector2Field(fieldName, (Vector2)value, guiLayoutOption);
            }
            else if (fieldType == typeof(Vector4))
            {
                newValue = EditorGUILayout.Vector4Field(fieldName, (Vector4)value, guiLayoutOption);
            }
            else if(fieldType == typeof(LayerMask))
            {
                List<string> layerNames = GetLayerNames();
                List<int> layerNumbers = GetLayerNumbers();

                LayerMask layerMask = (LayerMask)value;
                int mask = layerMask.value;

                int displayMask = 0;
                for (int i = 0; i < layerNumbers.Count; i++)
                {
                    if (((1 << layerNumbers[i]) & mask) != 0)
                    {
                        displayMask |= (1 << i);
                    }
                }
                displayMask = EditorGUILayout.MaskField(fieldName, displayMask, layerNames.ToArray());

                int newMask = 0;

                for (int i = 0; i < layerNumbers.Count; i++)
                {
                    if ((displayMask & (1 << i)) != 0)
                    {
                        newMask |= (1 << layerNumbers[i]);
                    }
                }

                newValue = (LayerMask)(newMask);
            }
            else if(fieldType == typeof(Color))
            {
                ColorUsageAttribute colorUsageAttribute = fieldInfo.GetCustomAttribute<ColorUsageAttribute>();

                bool showEyeDropper = true;
                bool showHdr = false;
                bool showAlpha = false;

                if(colorUsageAttribute != null)
                {
                    showHdr = colorUsageAttribute.hdr;
                    showAlpha = colorUsageAttribute.showAlpha;
                }
                GUIContent label = new GUIContent(fieldName);
                newValue = EditorGUILayout.ColorField(label, (Color)value, showEyeDropper, showAlpha, showHdr, guiLayoutOption);
            }

            else if(serializedLists.ContainsKey(fieldInfo))
            {
                ListInfo listInfo = serializedLists[fieldInfo];
                DrawListInEditor(fieldName, fieldInfo, fieldType, isEditable: true, ref listInfo, out newValue);
                serializedLists[fieldInfo] = listInfo;
            }
            else if(customTypeFieldToFoldoutMap.ContainsKey(fieldInfo))
            {
                bool foldoutValue = customTypeFieldToFoldoutMap[fieldInfo];

                BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo[] customDataTypeFields = fieldType.GetFields(flags);

                if (GetTotalWidthToBeOccupiedByCustomDataTypeFields(customDataTypeFields) > EditorGUIUtility.currentViewWidth - EditorGUIUtility.labelWidth - editorDebugModeConfigSO.ScrollbarWidth)
                {
                    newValue = DrawCustomTypeWithFoldout(fieldName, fieldType, value, customDataTypeFields, isEditable: true, ref foldoutValue);
                }
                else
                {
                    newValue = DrawCustomTypeInline(fieldName, value, customDataTypeFields, isEditable: true);
                }

                customTypeFieldToFoldoutMap[fieldInfo] = foldoutValue;
            }
        }

        private List<string> GetLayerNames()
        {
            List<string> layerNames = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                string name = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(name))
                {
                    layerNames.Add(name);
                }
            }
            return layerNames;
        }

        private List<int> GetLayerNumbers()
        {
            List<int> layerNumbers = new List<int>();
            for (int i = 0; i < 32; i++)
            {
                string name = LayerMask.LayerToName(i);

                if (!string.IsNullOrEmpty(name))
                {
                    layerNumbers.Add(i);
                }
            }
            return layerNumbers;
        }

        private void DrawListInEditor(string fieldName, FieldInfo fieldInfo, Type listType, bool isEditable, ref ListInfo listInfo, out object newValue)
        {
            Type elementType = listType.GetGenericArguments()[0];
            IList list = listInfo.ActualList;

            if (list == null)
            {
                var listElementType = typeof(List<>).MakeGenericType(elementType);
                list = (IList)Activator.CreateInstance(listElementType);
                fieldInfo.SetValue(target, list);
            }

            bool listFoldout = false;

            // We will define all the callbacks of the reorderable list when it's being drawn for the first time
            if (!nameToReorderableListsMap.ContainsKey(fieldName))
            {
                ReorderableList reorderableList = new ReorderableList(list, elementType, draggable: isEditable, displayHeader: false, displayAddButton: isEditable, displayRemoveButton: isEditable);

                for(int i = 0; i < list.Count; i++)
                {
                    string elementNameInDictionary = GetListElementNameInDictionary(fieldInfo, listInfo, i);
                    listElementFoldoutsMap.Add(elementNameInDictionary, EditorPrefs.GetBool(elementNameInDictionary, false));
                }

                AssignCallbacksToReorderableList(listInfo, fieldInfo, elementType, listInfo.ListElementName, reorderableList, isEditable);    

                nameToReorderableListsMap[fieldName] = reorderableList;
            }

            // Draw List based on foldout
            EditorGUILayout.BeginHorizontal();

            listFoldout = EditorGUILayout.Foldout(listInfo.ShowList, ObjectNames.NicifyVariableName(fieldInfo.Name), toggleOnLabelClick: true, boldFoldoutStyle);

            GUIStyle newStyle = new GUIStyle(GUI.skin.box);
            newStyle.alignment = TextAnchor.MiddleCenter;

            if (isEditable)
            {
                EditorGUI.BeginChangeCheck();

                Undo.RecordObject(target, "Modifying List Count");
                int count = EditorGUILayout.IntField(list.Count, GUILayout.Width(60f));

                if (EditorGUI.EndChangeCheck())
                {
                    while (count > list.Count)
                    {
                        Undo.RecordObject(target, "Add Element");
                        var defaultValue = elementType.IsValueType ? Activator.CreateInstance(elementType) : null;
                        list.Add(defaultValue);
                        EditorUtility.SetDirty(target);
                    }

                    while (count < list.Count)
                    {
                        Undo.RecordObject(target, "Remove Element");
                        list.RemoveAt(list.Count - 1);
                        EditorUtility.SetDirty(target);
                    }
                }
            }
            else
            {
                GUI.enabled = false;
                EditorGUILayout.IntField(list.Count, GUILayout.Width(60f));
                GUI.enabled = true;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5f);

            // if the foldout is pressed, draw the list
            if (listFoldout)
            {
                // This is from where the list starts to draw
                nameToReorderableListsMap[fieldName].DoLayoutList();

                ReorderableList reorderableList = nameToReorderableListsMap[fieldName];

                CheckForDragAndDropInList(ref reorderableList);

                nameToReorderableListsMap[fieldName] = reorderableList;
            }

            listInfo.ActualList = nameToReorderableListsMap[fieldName].list;
            listInfo.ShowList = listFoldout;

            newValue = list;
        }

        private void AssignCallbacksToReorderableList(ListInfo listInfo, FieldInfo fieldInfo, Type elementType, string elementName, ReorderableList reorderableList, bool isEditable)
        {
            // This is the callback that will be called when the list's header will be drawn
            reorderableList.drawHeaderCallback = rect =>
            {
            };

            // This is the callback that will be called when drawing an element
            reorderableList.drawElementCallback = (listRect, index, isActive, isFocused) =>
            {
                object element = listInfo.ActualList[index];

                // This will draw the list element based on the rect that is passed to the function
                string listElementKey = GetListElementNameInDictionary(fieldInfo, listInfo, index);
                object newValue = default;

                if (isEditable)
                {
                    newValue = DrawListElement(fieldInfo, listRect, element, elementType, $"{elementName} {index}", listElementKey);
                }
                else
                {
                    GUI.enabled = false;
                    DrawListElement(fieldInfo, listRect, element, elementType, $"{elementName} {index}", listElementKey);
                    GUI.enabled = true;
                }
                
                if (isEditable)
                {
                    listInfo.ActualList[index] = newValue;
                    EditorUtility.SetDirty(target);
                }
            };

            reorderableList.onReorderCallback = list =>
            {
                ResetFoldoutDataForList(list, fieldInfo, listInfo, fieldInfo.Name);
            };

            reorderableList.elementHeightCallback = index =>
            {
                string elementKey = GetListElementNameInDictionary(fieldInfo, listInfo, index);
                if (listElementFoldoutsMap.TryGetValue(elementKey, out bool value) && value)
                {
                    float lineHeight = EditorGUIUtility.singleLineHeight + 10f;
                    return lineHeight * elementType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic).Length;
                }
                else
                {
                    return EditorGUIUtility.singleLineHeight;
                }
            };

            // Callback when you add something to the list
            reorderableList.onAddCallback = reorderableList =>
            {
                object defaultValue = elementType.IsValueType ? Activator.CreateInstance(elementType) : null;

                if(reorderableList.count > 0)
                {
                    defaultValue = reorderableList.list[^1];
                }
                Undo.RecordObject(target, "Add Element");
                listInfo.ActualList.Add(defaultValue);

                if(listInfo.IsCustomDataTypeList)
                {
                    string elementNameInDictionary = GetListElementNameInDictionary(fieldInfo, listInfo, listInfo.ActualList.Count - 1);
                    listElementFoldoutsMap.Add(elementNameInDictionary, EditorPrefs.GetBool(elementNameInDictionary, false));
                }

                EditorUtility.SetDirty(target);
            };

            // Callback when you remove something from the list
            reorderableList.onRemoveCallback = rl =>
            {
                Undo.RecordObject(target, "Remove Element");
                listInfo.ActualList.RemoveAt(rl.index);

                ResetFoldoutDataForList(reorderableList, fieldInfo, listInfo, fieldInfo.Name);

                EditorUtility.SetDirty(target);
            };
        }

        private void HandleFieldAsShowIfField(FieldInfo field, out bool canDraw)
        {
            ShowIfAttribute showIfAttribute = field.GetCustomAttribute<ShowIfAttribute>();

            if (showIfAttribute == null)
            {
                canDraw = true;
                return;
            }

            string conditionFieldName = showIfAttribute.ConditionFieldName;
            object conditionFieldValue = showIfAttribute.ConditionFieldValue;

            Type type = target.GetType();

            FieldInfo conditionField = type.GetField(conditionFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (conditionField == null)
            {
                Debug.LogError($"Condition field with the name \"{conditionFieldName}\" Doesn't exist! \n Check if you have typed the name properly in the class!");

                canDraw = false;
                return;
            }
            else if (conditionField.FieldType != conditionFieldValue.GetType())
            {
                Debug.LogError($"The condition field value is of type {conditionFieldValue.GetType().Name}, which doesn't match with type {conditionField.FieldType.Name} of the condition field \"{conditionFieldName}\""!);

                canDraw = false;
                return;
            }

            canDraw = conditionField.GetValue(target).ToString() == conditionFieldValue.ToString();
        }

        private void ResetFoldoutDataForList(ReorderableList reorderableList, FieldInfo fieldInfo, ListInfo listInfo, string listPrefix)
        {
            RemoveAllKeysWithPrefix(listPrefix);

            for(int i = 0; i < reorderableList.list.Count; i++)
            {
                string elementNameInDictionary = GetListElementNameInDictionary(fieldInfo, listInfo, i);

                listElementFoldoutsMap.Add(elementNameInDictionary, EditorPrefs.GetBool(elementNameInDictionary, false));
                EditorPrefs.SetBool(elementNameInDictionary, false);
            }
        }

        private void RemoveAllKeysWithPrefix(string listPrefix)
        {
            List<string> keysToRemove = new List<string>();
            Dictionary<string, bool> tempFoldoutsMap = new Dictionary<string ,bool>();

            foreach(var pair in listElementFoldoutsMap)
            {
                if(!pair.Key.Contains(listPrefix))
                {
                    tempFoldoutsMap.Add(pair.Key, pair.Value);
                }
            }
            listElementFoldoutsMap = new Dictionary<string, bool>(tempFoldoutsMap);
        }

        private string GetListElementNameInDictionary(FieldInfo fieldInfo, ListInfo listInfo, int index)
        {
            return $"{fieldInfo.Name}_{listInfo.ListElementName}_{index}";
        }

        private void CheckForDragAndDropInList(ref ReorderableList reorderableList)
        {
            Rect dropArea = GUILayoutUtility.GetLastRect();
            Event evt = Event.current;

            if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && dropArea.Contains(evt.mousePosition))
            {
                Type elementType = reorderableList.list.GetType().GetGenericArguments()[0];

                if (elementType.IsPrimitive) return;

                bool canDrop = true;
                foreach (Object droppingObject in DragAndDrop.objectReferences)
                {
                    if (droppingObject.GameObject() && IsTypeInheritingFromMonoBehaviourOrScriptableObject(elementType))
                    {
                        Component component = droppingObject.GameObject().GetComponent(elementType);

                        if (component == null)
                        {
                            canDrop = false;
                            break;
                        }
                    }
                    else
                    {
                        if (droppingObject.GetType() != elementType)
                        {
                            canDrop = false;
                            break;
                        }
                    }
                }

                DragAndDrop.visualMode = canDrop ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                if (evt.type == EventType.DragPerform && canDrop)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (Object droppedObject in DragAndDrop.objectReferences)
                    {
                        if (droppedObject.GameObject())
                        {
                            reorderableList.list.Add(droppedObject.GameObject().GetComponent(elementType));
                        }
                        else
                        {
                            reorderableList.list.Add(droppedObject);
                        }
                    }
                    EditorUtility.SetDirty(target);
                }

                evt.Use();
            }
        }

        private object DrawListElement(FieldInfo fieldInfo, Rect rect, object element, Type elementType, string elementName, string elementNameInDictionary)
        {
            object newValue = default;

            var style = new GUIStyle(GUI.skin.label);

            float labelWidth = EditorGUIUtility.labelWidth;

            Rect labelRect = new Rect(rect.x, rect.y, labelWidth, EditorGUIUtility.singleLineHeight);
            Rect fieldRect = new Rect(rect.x + labelWidth, rect.y, rect.width - labelWidth, EditorGUIUtility.singleLineHeight);

            if (!IsFieldCustomClassType(elementType, element) && !IsFieldStructType(elementType))
            {
                if (elementName != "")
                {
                    EditorGUI.LabelField(labelRect, elementName);
                }

                if (elementType == typeof(string))
                {
                    newValue = EditorGUI.TextField(fieldRect, element?.ToString() ?? "");
                }
                else if (elementType == typeof(int))
                {
                    int value = element != null ? (int)element : 0;
                    DraggableIntField(fieldRect, ref value);
                    newValue = value;
                }
                else if (elementType == typeof(float))
                {
                    float value = element != null ? (float)element : 0f;
                    DraggableFloatField(fieldRect, ref value);
                    newValue = value;
                }
                else if (typeof(Object).IsAssignableFrom(elementType))
                {
                    newValue = EditorGUI.ObjectField(fieldRect, (Object)element, elementType, true);
                }
                else if (elementType.IsEnum)
                {
                    newValue = EditorGUI.EnumPopup(fieldRect, (Enum)element);
                }
                else if (elementType == typeof(AnimationCurve))
                {
                    newValue = EditorGUI.CurveField(fieldRect, (AnimationCurve)element);
                }
                else if (elementType == typeof(Vector3))
                {
                    newValue = EditorGUI.Vector3Field(fieldRect, "", (Vector3)element);
                }
                else if (elementType == typeof(Vector2))
                {
                    newValue = EditorGUI.Vector2Field(fieldRect, "", (Vector2)element);
                }
                else if (elementType == typeof(Vector4))
                {
                    newValue = EditorGUI.Vector4Field(fieldRect, "", (Vector4)element);
                }
                else if (elementType == typeof(LayerMask))
                {
                    List<string> layerNames = GetLayerNames();
                    List<int> layerNumbers = GetLayerNumbers();

                    LayerMask layerMask = (LayerMask)element;
                    int mask = layerMask.value;

                    int displayMask = 0;
                    for (int i = 0; i < layerNumbers.Count; i++)
                    {
                        if (((1 << layerNumbers[i]) & mask) != 0)
                        {
                            displayMask |= (1 << i);
                        }
                    }
                    displayMask = EditorGUI.MaskField(fieldRect, displayMask, layerNames.ToArray());

                    int newMask = 0;

                    for (int i = 0; i < layerNumbers.Count; i++)
                    {
                        if ((displayMask & (1 << i)) != 0)
                        {
                            newMask |= (1 << layerNumbers[i]);
                        }
                    }

                    newValue = (LayerMask)(newMask);
                }
                else if (elementType == typeof(Color))
                {
                    ColorUsageAttribute colorUsageAttribute = fieldInfo.GetCustomAttribute<ColorUsageAttribute>();

                    bool showEyeDropper = true;
                    bool showHdr = false;
                    bool showAlpha = false;

                    if (colorUsageAttribute != null)
                    {
                        showHdr = colorUsageAttribute.hdr;
                        showAlpha = colorUsageAttribute.showAlpha;
                    }
                    GUIContent label = new GUIContent("");
                    newValue = EditorGUI.ColorField(fieldRect, label, (Color)element, showEyeDropper, showAlpha, showHdr);
                }
            }
            else if (IsFieldCustomClassType(elementType, element) || IsFieldStructType(elementType))
            {
                newValue = DrawCustomDataTypeInList(rect, elementType, element, elementName, elementNameInDictionary);
            }

            return newValue;
        }

        private float GetDraggableCursorDelta(Rect rect)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUIUtility.AddCursorRect(rect, MouseCursor.SlideArrow);

            int controlID = GUIUtility.GetControlID(FocusType.Passive, rect);

            float value = 0f;
            Event evt = Event.current;

            switch (evt.GetTypeForControl(controlID))
            {
                case EventType.MouseDown:
                    if (rect.Contains(evt.mousePosition) && evt.button == 0)
                    {
                        GUIUtility.hotControl = controlID;
                        evt.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlID)
                    {
                        float delta = evt.delta.x;
                        value += Mathf.RoundToInt(delta);
                        evt.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlID)
                    {
                        GUIUtility.hotControl = 0;
                        evt.Use();
                    }
                    break;
            }

            return value;
        }

        private void DraggableFloatField(Rect rect, ref float value)
        {
            Rect draggableRect = new Rect(rect.x, rect.y, 40f, EditorGUIUtility.singleLineHeight);
            float delta = GetDraggableCursorDelta(draggableRect);

            value += (delta);
            value = EditorGUI.FloatField(rect, value);

            EditorGUILayout.EndHorizontal();
        }

        private void DraggableIntField(Rect rect, ref int value)
        {
            Rect draggableRect = new Rect(rect.x, rect.y, 40f, EditorGUIUtility.singleLineHeight);
            float delta = GetDraggableCursorDelta(draggableRect);

            value += Mathf.RoundToInt(delta);
            value = EditorGUI.IntField(rect, value);

            EditorGUILayout.EndHorizontal();
        }

        private object DrawCustomDataTypeInList(Rect rect, Type fieldType, object element, string elementName, string elementNameInDictionary)
        {
            bool foldoutValue = false;

            if (listElementFoldoutsMap.ContainsKey(elementNameInDictionary))
            {
                foldoutValue = listElementFoldoutsMap[elementNameInDictionary];
            }
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo[] customDataTypeFields = fieldType.GetFields(flags);

            EditorGUI.indentLevel++;

            float originalWidth = rect.height;
            rect.height = EditorGUIUtility.singleLineHeight;
            foldoutValue = EditorGUI.Foldout(rect, foldoutValue, ObjectNames.NicifyVariableName(elementName), toggleOnLabelClick: true);

            rect.height = originalWidth;

            if (foldoutValue)
            {
                EditorGUI.indentLevel++;
                for(int i = 0; i < customDataTypeFields.Length; i++)
                {
                    FieldInfo customDataTypeField = customDataTypeFields[i];

                    Rect newRect = new Rect(rect.x, rect.y + (i + 1) * 20f, rect.width, rect.height);

                    object value = customDataTypeField.GetValue(element);
                    object newValue = DrawListElement(customDataTypeField, newRect, value, customDataTypeField.FieldType, ObjectNames.NicifyVariableName(customDataTypeField.Name), "");

                    customDataTypeField.SetValue(element, newValue);
                }
                EditorGUI.indentLevel--;
            }

            EditorGUI.indentLevel--;
            listElementFoldoutsMap[elementNameInDictionary] = foldoutValue;
            EditorPrefs.SetBool(elementNameInDictionary, foldoutValue);
            return element;
        }

        private float GetTotalWidthToBeOccupiedByCustomDataTypeFields(FieldInfo[] customDataTypeFields)
        {
            float totalWidth = 50f;
            var style = new GUIStyle(GUI.skin.label);

            foreach (FieldInfo field in customDataTypeFields)
            {
                Vector2 size = style.CalcSize(new GUIContent(field.Name));

                totalWidth += size.x;
                totalWidth += GetWidthBasedOnTypeOfField(field.FieldType);
            }

            return totalWidth;
        }

        private float GetWidthBasedOnTypeOfField(Type fieldType)
        {
            if (fieldType == typeof(int))
            {
                return editorDebugModeConfigSO.IntFieldWidth;
            }
            else if (fieldType == typeof(float))
            {
                return editorDebugModeConfigSO.FloatFieldWidth;
            }
            else if (fieldType == typeof(bool))
            {
                return editorDebugModeConfigSO.BoolFieldWidth;
            }
            else if (typeof(Object).IsAssignableFrom(fieldType))
            {
                return editorDebugModeConfigSO.ObjectFieldWidth;
            }
            else
            {
                return editorDebugModeConfigSO.DefaultWidth;
            }
        }

        private bool IsFieldStructType(Type fieldType)
        {
            return (!fieldType.Namespace.Contains("UnityEngine") && fieldType.IsValueType && !fieldType.IsPrimitive && !fieldType.IsEnum); // Structs
        }

        private bool IsFieldCustomClassType(Type fieldType, object value)
        {
            return (!fieldType.Namespace.Contains("UnityEngine") && fieldType.IsClass && value != null && !IsTypeInheritingFromMonoBehaviourOrScriptableObject(fieldType));
        }

        private bool IsTypeInheritingFromMonoBehaviourOrScriptableObject(Type fieldType)
        {
            return typeof(MonoBehaviour).IsAssignableFrom(fieldType) || typeof(ScriptableObject).IsAssignableFrom(fieldType);
        }

        private void DrawScriptObject()
        {
            EditorGUI.BeginDisabledGroup(true);

            var monoScript = (target as MonoBehaviour) != null
                ? MonoScript.FromMonoBehaviour((MonoBehaviour)target)
                : MonoScript.FromScriptableObject((ScriptableObject)target);

            EditorGUILayout.ObjectField("Script", monoScript, GetType(), false);

            EditorGUI.EndDisabledGroup();
        }

        private void AssignGUIStyles()
        {
            if (centeredBoldLabelStyle == null)
            {
                centeredBoldLabelStyle = new GUIStyle(GUI.skin.label);
                centeredBoldLabelStyle.alignment = TextAnchor.MiddleCenter;
                centeredBoldLabelStyle.fontStyle = FontStyle.Bold;
            }

            if (boldLabelStyle == null)
            {
                boldLabelStyle = new GUIStyle(GUI.skin.label);
                boldLabelStyle.fontStyle = FontStyle.Bold;
            }

            if (boldFoldoutStyle == null)
            {
                boldFoldoutStyle = new GUIStyle(EditorStyles.foldout);
                boldFoldoutStyle.fontStyle = FontStyle.Bold;
            }
        }

        #endregion

        public override void OnInspectorGUI()
        {
            DrawScriptObject();

            if (editorDebugModeConfigSO == null)
            {
                editorDebugModeConfigSO = AssetDatabase.LoadAssetAtPath<EditorDebugModeConfigSO>("Assets/Custom Attributes Config/Editor Debug Mode Config.asset");

                if(editorDebugModeConfigSO == null)
                {
                    editorDebugModeConfigSO = CreateInstance<EditorDebugModeConfigSO>();

                    AssetDatabase.CreateFolder("Assets", "Custom Attributes Config");
                    AssetDatabase.CreateAsset(editorDebugModeConfigSO, "Assets/Custom Attributes Config/Editor Debug Mode Config.asset");

                    AssetDatabase.SaveAssets();
                }
            }

            AssignGUIStyles();

            HandleFoldouts();
            HandleButtonMethods();
            HandleDebugMode();

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
}
#endif