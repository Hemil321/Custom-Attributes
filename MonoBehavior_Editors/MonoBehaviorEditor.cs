using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CustomAttributes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ReverseTowerDefense
{
    [CustomEditor(typeof(MonoBehaviour), true)]
    public class MonoBehaviorEditor : Editor
    {
        public struct ListInfo
        {
            public IList actualList;
            public bool showList;
            public FieldInfo field;
            public Type fieldType;
            public float heightPerElement;
            public bool isHeightCalculated;

            public ListInfo(IList ActualList, bool ShowList, FieldInfo Field, Type FieldType)
            {
                actualList = ActualList;
                showList = ShowList;
                field = Field;
                fieldType = FieldType;
                heightPerElement = 0f;

                isHeightCalculated = false;
            }
        }

        private static GUIStyle centeredBoldLabelStyle;
        private static GUIStyle boldLabelStyle;

        #region Private Fields Handlers

        private bool isPreviewingAnything = false;

        #region Private Fields Inspector Parameters

        private readonly string showPrivateStuffWithInstanceID = "Show Private Stuff_{0}";
        private Dictionary<FieldInfo, ListInfo> privateFieldToListMap = new Dictionary<FieldInfo, ListInfo>();
        private Dictionary<FieldInfo, bool> customTypePrivateFieldsFoldoutMap = new Dictionary<FieldInfo, bool>();
        private List<FieldInfo> privateFieldsList;
        private bool showPrivateFields = false;

        private EditorDebugModeConfigSO editorDebugModeConfigSO;

        #endregion

        #region Helper Functions

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
                        customTypePrivateFieldsFoldoutMap.Add(field, false);
                    }
                }
                else if (field.GetCustomAttribute<ReadonlyAttribute>() != null)
                {
                    fieldsList.Add(field);

                    if (IsFieldStructType(field.FieldType) || IsFieldCustomClassType(field.FieldType, field.GetValue(target)))
                    {
                        customTypePrivateFieldsFoldoutMap.Add(field, false);
                    }
                }
            }
        }

        private void DrawNonEditableFieldInEditor(string fieldName, Type fieldType, object value, FieldInfo fieldInfo)
        {
            DrawFieldInEditor(fieldName, fieldType, value, GUILayout.MaxWidth(1000f), out object newValue);

            if (newValue == default && customTypePrivateFieldsFoldoutMap.ContainsKey(fieldInfo)) // Handle structs and classes
            {
                bool foldoutValue = customTypePrivateFieldsFoldoutMap[fieldInfo];

                int numberOfFieldsInCustomType = GetNumberOfFieldsInCustomType(fieldType);
                BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
                FieldInfo[] customDataTypeFields = fieldType.GetFields(flags);

                if (GetTotalWidthToBeOccupiedByCustomDataTypeFields(customDataTypeFields) > EditorGUIUtility.currentViewWidth - EditorGUIUtility.labelWidth - editorDebugModeConfigSO.ScrollbarWidth)
                {
                    DrawNonEditableCustomTypeWithFoldout(fieldName, fieldType, value, customDataTypeFields, ref foldoutValue);
                }
                else
                {
                    DrawNonEditableCustomTypeInline(fieldName, fieldType, value, customDataTypeFields);
                }

                customTypePrivateFieldsFoldoutMap[fieldInfo] = foldoutValue;
            }
        }

        private int GetNumberOfFieldsInCustomType(Type customFieldType)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            return customFieldType.GetFields(flags).Length;
        }

        private void DrawNonEditableCustomTypeInline(string fieldName, Type fieldType, object value, FieldInfo[] customDataTypeFields)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(fieldName, GUILayout.Width(EditorGUIUtility.labelWidth));

            foreach (FieldInfo field in customDataTypeFields)
            {
                DrawCustomClassField(ObjectNames.NicifyVariableName(field.Name), field.FieldType, field.GetValue(value), field);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawCustomClassField(string fieldName, Type fieldType, object value, FieldInfo fieldInfo)
        {
            var style = new GUIStyle(GUI.skin.label);

            Vector2 size = style.CalcSize(new GUIContent(fieldName));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(fieldName, GUILayout.Width(size.x));

            DrawFieldInEditor("", fieldType, value, GUILayout.MinWidth(GetWidthBasedOnTypeOfField(fieldType)), out object newValue);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawNonEditableCustomTypeWithFoldout(string fieldName, Type fieldType, object value, FieldInfo[] customDataTypeFields, ref bool customTypeFoldout)
        {
            GUI.enabled = true;
            customTypeFoldout = EditorGUILayout.Foldout(customTypeFoldout, fieldName);
            GUI.enabled = false;

            if (customTypeFoldout)
            {
                EditorGUI.indentLevel++;

                foreach (FieldInfo customClassField in customDataTypeFields)
                {
                    DrawNonEditableFieldInEditor(ObjectNames.NicifyVariableName(customClassField.Name), customClassField.FieldType, customClassField.GetValue(value), customClassField);
                }

                EditorGUI.indentLevel--;
            }
        }

        private void DrawFields(List<FieldInfo> listOfFieldsToDraw, Dictionary<FieldInfo, ListInfo> fieldToListMap)
        {
            foreach (FieldInfo readonlyField in listOfFieldsToDraw)
            {
                if (fieldToListMap.ContainsKey(readonlyField))
                {
                    ListInfo listInfo = fieldToListMap[readonlyField];
                    DrawList(readonlyField, ref listInfo);
                    fieldToListMap[readonlyField] = listInfo;

                    continue;
                }

                string fieldName = ObjectNames.NicifyVariableName(readonlyField.Name);
                object value = readonlyField.GetValue(target);
                Type fieldType = readonlyField.FieldType;

                if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    IList list = value as IList;
                    ListInfo newListInfo = new ListInfo(list, false, readonlyField, fieldType);

                    DrawList(readonlyField, ref newListInfo);

                    fieldToListMap.Add(readonlyField, newListInfo);
                }

                GUI.enabled = false;

                DrawNonEditableFieldInEditor(fieldName, fieldType, value, readonlyField);

                GUI.enabled = true;
            }
        }

        private void DrawList(FieldInfo fieldInfo, ref ListInfo listInfo)
        {
            Vector2 scrollPosition = Vector2.zero;
            IList list = listInfo.actualList;

            Type elementType = listInfo.fieldType.GetGenericArguments()[0];

            GUI.enabled = true;

            EditorGUILayout.BeginHorizontal();

            listInfo.showList = EditorGUILayout.Foldout(listInfo.showList, ObjectNames.NicifyVariableName(listInfo.field.Name));
            int count = list == null ? 0 : list.Count;
            GUI.enabled = false;

            GUIStyle newStyle = new GUIStyle(GUI.skin.box);
            newStyle.alignment = TextAnchor.MiddleCenter;
            EditorGUILayout.IntField(count, newStyle, GUILayout.Width(20f));

            EditorGUILayout.EndHorizontal();

            if (listInfo.showList)
            {
                EditorGUI.indentLevel++;

                if (list != null && list.Count > 0)
                {
                    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(100f));

                    for (int i = 0; i < list.Count; i++)
                    {
                        object item = list[i];

                        EditorGUILayout.BeginVertical(GUI.skin.box);

                        DrawNonEditableFieldInEditor($"Element {i}", elementType, item, fieldInfo);

                        EditorGUILayout.EndVertical();
                    }

                    GUILayout.EndScrollView();
                }
                else
                {
                    EditorGUILayout.HelpBox("List is Empty!", MessageType.Info);
                }

                EditorGUI.indentLevel--;
            }
        }

        private void HandlePrivateFields()
        {
            if (privateFieldsList.Count > 0)
            {
                GUILayout.Space(5f);

                if (showPrivateFields)
                {
                    DrawFields(privateFieldsList, privateFieldToListMap);
                }
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

        private bool IsFieldStructType(Type fieldType)
        {
            return (fieldType.IsValueType && !fieldType.IsPrimitive); // Structs
        }

        private bool IsFieldCustomClassType(Type fieldType, object value)
        {
            return (fieldType.IsClass && value != null);
        }

        #endregion

        #endregion

        #region ShowIf Attribute 

        private List<FieldInfo> showIfFieldsList;

        private void PopulateShowIfFields()
        {
            showIfFieldsList = new List<FieldInfo>();

            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

            foreach (FieldInfo field in type.GetFields(flags))
            {
                if (field.GetCustomAttribute<ShowIfAttribute>() != null)
                {
                    showIfFieldsList.Add(field);
                }
            }
        }

        private void DrawShowIfFields()
        {
            foreach (FieldInfo field in showIfFieldsList)
            {
                ShowIfAttribute showIfAttribute = field.GetCustomAttribute<ShowIfAttribute>();

                if (showIfAttribute == null) continue;

                string conditionFieldName = showIfAttribute.ConditionFieldName;
                object conditionFieldValue = showIfAttribute.ConditionFieldValue;

                Type type = target.GetType();

                FieldInfo conditionField = type.GetField(conditionFieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (conditionField == null)
                {
                    this.LogError($"Condition field with the name \"{conditionFieldName}\" Doesn't exist! \n Check if you have typed the name properly in the class!");

                    continue;
                }
                else if (conditionField.FieldType != conditionFieldValue.GetType())
                {
                    this.LogError($"The condition field value is of type {conditionFieldValue.GetType().Name}, which doesn't match with type {conditionField.FieldType.Name} of the condition field \"{conditionFieldName}\""!);

                    continue;
                }

                if (conditionField.GetValue(target).ToString() == conditionFieldValue.ToString())
                {
                    DrawFieldInEditor(ObjectNames.NicifyVariableName(field.Name), field.FieldType, field.GetValue(target), GUILayout.MaxWidth(1000f), out object newValue);

                    field.SetValue(target, newValue);
                }
            }
        }

        private void HandleShowIfFields()
        {
            if (showIfFieldsList == null)
            {
                PopulateShowIfFields();
            }

            DrawShowIfFields();
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

        #region Main Helper Functions

        private void DrawFieldInEditor(string fieldName, Type fieldType, object value, GUILayoutOption guiLayoutOption, out object newValue)
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

        #endregion

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (editorDebugModeConfigSO == null)
            {
                editorDebugModeConfigSO = AssetDatabase.LoadAssetAtPath<EditorDebugModeConfigSO>("Assets/CustomAttributes/Config File/Editor Debug Mode Config.asset");
            }

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

            HandleShowIfFields();
            HandleButtonMethods();

            HandleDebugMode();

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
}