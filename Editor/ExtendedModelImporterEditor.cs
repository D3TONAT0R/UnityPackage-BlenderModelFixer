﻿using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif

namespace D3TEditor.BlenderModelFixer
{
	[CustomEditor(typeof(ModelImporter)), CanEditMultipleObjects]
	public class ExtendedModelImporterEditor : AssetImporterEditor
	{
		public enum SupportState
		{
			None,
			Partial,
			All
		}

		private static readonly string[] tabNames = new string[] { "Model", "Rig", "Animation", "Materials" };

		private object[] tabs;
		private int activeTabIndex;

		private SupportState supportState;

		public override void OnEnable()
		{
			var param = new object[] { this };
			tabs = new object[]
			{
			Activator.CreateInstance(Type.GetType("UnityEditor.ModelImporterModelEditor, UnityEditor"), param),
			Activator.CreateInstance(Type.GetType("UnityEditor.ModelImporterRigEditor, UnityEditor"), param),
			Activator.CreateInstance(Type.GetType("UnityEditor.ModelImporterClipEditor, UnityEditor"), param),
			Activator.CreateInstance(Type.GetType("UnityEditor.ModelImporterMaterialEditor, UnityEditor"), param),
			};

			foreach(var tab in tabs)
			{
				tab.GetType().GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(tab, new object[0]);
			}
			activeTabIndex = EditorPrefs.GetInt(GetType().Name + "ActiveEditorIndex");

			int fixableModels = 0;
			for(int i = 0; i < targets.Length; i++)
			{
				if(BlenderFBXPostProcessor.IsModelValidForFix(AssetDatabase.GetAssetPath(targets[i])))
				{
					fixableModels++;
				}
			}
			supportState = fixableModels == targets.Length ? SupportState.All : fixableModels > 0 ? SupportState.Partial : SupportState.None;

			base.OnEnable();
		}

		public override void OnDisable()
		{
			base.OnDisable();
			foreach(var tab in tabs)
			{
				tab.GetType().GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(tab, new object[0]);
			}
		}

		public override void OnInspectorGUI()
		{
			serializedObject.Update();
			extraDataSerializedObject?.Update();

			DrawTabHeader();

			if(activeTabIndex == 0) DrawBlenderImportFixProperties();

			DrawActiveBuiltinTab();

			serializedObject.ApplyModifiedProperties();
			extraDataSerializedObject.ApplyModifiedProperties();

			ApplyRevertGUI();

			if(GUILayout.Button("Force apply"))
			{
				ApplyAndImport();
			}
		}

		private void DrawBlenderImportFixProperties()
		{
			if(supportState != SupportState.None)
			{
				GUILayout.Label("Blender Import Fixes", EditorStyles.boldLabel);
				GUIContent label = new GUIContent("Apply Axis Conversion");

				if(supportState == SupportState.All)
				{
					var property = extraDataSerializedObject.FindProperty(nameof(BlenderFixesExtraData.applyFix));
					EditorGUILayout.PropertyField(property, label);
				}
				else
				{
					EditorGUILayout.LabelField(label, new GUIContent("(Not all selected models can be fixed)"));
				}
			}
		}

		private void DrawTabHeader()
		{
			// Always allow user to switch between tabs even when the editor is disabled, so they can look at all parts
			// of read-only assets
			using(new EditorGUI.DisabledScope(false)) // this doesn't enable the UI, but it seems correct to push the stack
			{
				GUI.enabled = true;
				using(new GUILayout.HorizontalScope())
				{
					GUILayout.FlexibleSpace();
					using(var check = new EditorGUI.ChangeCheckScope())
					{
						activeTabIndex = GUILayout.Toolbar(activeTabIndex, tabNames, "LargeButton", GUI.ToolbarButtonSize.FitToContents);
						if(check.changed)
						{
							EditorPrefs.SetInt(GetType().Name + "ActiveEditorIndex", activeTabIndex);
							tabs[activeTabIndex].GetType().GetMethod("OnInspectorGUI").Invoke(tabs[activeTabIndex], new object[0]);
						}
					}
					GUILayout.FlexibleSpace();
				}
			}
		}

		private void DrawActiveBuiltinTab()
		{
			var activeTab = tabs[activeTabIndex];
			activeTab.GetType().GetMethod("OnInspectorGUI").Invoke(activeTab, new object[0]);
		}

		protected override void Apply()
		{
			for(int i = 0; i < targets.Length; i++)
			{
				var d = (BlenderFixesExtraData)extraDataTargets[i];
				var ud = AssetUserData.Get(targets[i]);
				ud.SetValue(BlenderFBXPostProcessor.postProcessorUserDataKey, d.applyFix);
				ud.ApplyModified(targets[i]);
			}
			base.Apply();
		}

		protected override Type extraDataType => typeof(BlenderFixesExtraData);

		protected override void InitializeExtraDataInstance(UnityEngine.Object extraData, int targetIndex)
		{
			var d = (BlenderFixesExtraData)extraData;
			d.applyFix = AssetUserData.Get(targets[targetIndex]).GetBool(BlenderFBXPostProcessor.postProcessorUserDataKey, false);
		}
	} 
}
