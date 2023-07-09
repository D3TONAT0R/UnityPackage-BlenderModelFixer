using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace D3TEditor.BlenderModelFixer
{
	public class BlenderFBXPostProcessor : AssetPostprocessor
	{
		public const string postProcessorUserDataKey = "applyBlenderAxisConversion";

		void OnPostprocessModel(GameObject root)
		{
			var mi = assetImporter as ModelImporter;
			if(ShouldApplyBlenderTransformFix(mi))
			{
				//Debug.Log("Applying fix on "+root.name);
				List<Mesh> meshes = new List<Mesh>();
				LocateMeshes(meshes, root.transform);

				foreach(var t in root.GetComponentsInChildren<Transform>(true))
				{
					if(t == null) continue;
					if(ShouldDeleteObject(t))
					{
						Object.DestroyImmediate(t.gameObject);
					}
					else
					{
						ApplyTransformFix(t);
					}
				}

				Matrix4x4 matrix = Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0));
				foreach(var mesh in meshes)
				{
					ApplyMeshFix(mesh, matrix, mi.importTangents != ModelImporterTangents.None);
				}
			}
			mi.SaveAndReimport();
		}

		private bool ShouldDeleteObject(Transform obj)
		{
			bool delete = false;
			if(obj.TryGetComponent<MeshRenderer>(out var renderer))
			{
				delete = !renderer.enabled;
				delete &= obj.childCount == 0;
			}
			return delete;
		}

		private void LocateMeshes(List<Mesh> list, Transform transform)
		{
			foreach(var filter in transform.GetComponentsInChildren<MeshFilter>(true))
			{
				if(filter.sharedMesh && !list.Contains(filter.sharedMesh))
				{
					list.Add(filter.sharedMesh);
				}
			}
		}

		private void ApplyMeshFix(Mesh m, Matrix4x4 matrix, bool calculateTangents)
		{
			//Debug.Log("Fixing mesh: " + m.name);
			var verts = m.vertices;
			var normals = m.normals;
			//var tangents = m.tangents;
			for(int i = 0; i < verts.Length; i++)
			{
				verts[i] = matrix.MultiplyPoint(verts[i]);
				normals[i] = matrix.MultiplyPoint(normals[i]);
			}

			/*
			for(int i = 0; i < tangents.Length; i++)
			{
				tangents[i] = matrix.MultiplyPoint3x4(tangents[i]);
			}
			*/
			m.SetVertices(verts);
			m.SetNormals(normals);
			//m.SetTangents(tangents);
			if(calculateTangents)
			{
				m.RecalculateTangents();
			}
			m.RecalculateBounds();
		}

		private void ApplyTransformFix(Transform t)
		{
			//Debug.Log("Fixing transform: " + t.name);
			var fix = new Vector3(-1, 1, -1);
			t.localPosition = Vector3.Scale(t.localPosition, fix);
			t.localEulerAngles = Vector3.Scale(t.localEulerAngles, fix);
			if(t.name.EndsWith("|Dupli|"))
			{
				Debug.Log("fixing " + t.name);
				t.localPosition = new Vector3(t.localPosition.x, -t.localPosition.z, -t.localPosition.y);
				//t.localEulerAngles = Vector3.Scale(t.localEulerAngles, -fix);
				//t.eulerAngles += new Vector3(89.98f, 0, 0);
			}
		}

		private bool ShouldApplyBlenderTransformFix(ModelImporter mi)
		{
			if(IsModelValidForFix(mi.assetPath))
			{
				return AssetUserData.Deserialize(mi.userData).GetBool(postProcessorUserDataKey, false);
			}
			else
			{
				return false;
			}
		}

		public static bool IsModelValidForFix(string assetPath)
		{
			if(assetPath.EndsWith(".blend", System.StringComparison.OrdinalIgnoreCase)) return true;
			if(File.ReadAllText(assetPath).Contains("Blender (stable FBX IO)")) return true;
			return false;
		}
	} 
}
