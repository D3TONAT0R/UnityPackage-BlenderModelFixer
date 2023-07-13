using System.Collections.Generic;
using System.IO;
using System.Linq;
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

				var transformStore = new Dictionary<Transform, (Vector3, Quaternion)>();
				foreach(var t in root.GetComponentsInChildren<Transform>(true))
				{
					if(t == root.transform) continue;
					transformStore.Add(t, (t.position, t.rotation));
				}

				var transforms = transformStore.Keys.ToArray();
				for(int i = 0; i < transforms.Length; i++)
				{
					var t = transforms[i];
					if(t == null || t == root.transform) continue;
					if(ShouldDeleteObject(t))
					{
						Object.DestroyImmediate(t.gameObject);
					}
					else
					{
						var stored = transformStore[t];
						ApplyTransformFix(t, stored.Item1, stored.Item2);
					}
				}

				Matrix4x4 matrix = Matrix4x4.Rotate(Quaternion.Euler(-89.98f, 180, 0));
				foreach(var mesh in meshes)
				{
					ApplyMeshFix(mesh, matrix, mi.importTangents != ModelImporterTangents.None);
				}
				Debug.Log("fixed it");
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
			foreach(var skinnedRenderer in transform.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if(skinnedRenderer.sharedMesh && !list.Contains(skinnedRenderer.sharedMesh))
				{
					list.Add(skinnedRenderer.sharedMesh);
				}
			}
		}

		private void ApplyMeshFix(Mesh m, Matrix4x4 matrix, bool calculateTangents)
		{
			Debug.Log("Fixing mesh: " + m.name);
			var verts = m.vertices;
			for(int i = 0; i < verts.Length; i++)
			{
				verts[i] = matrix.MultiplyPoint(verts[i]);
			}
			m.vertices = verts;

			if(m.normals != null)
			{
				var normals = m.normals;
				for(int i = 0; i < verts.Length; i++)
				{
					normals[i] = matrix.MultiplyPoint(normals[i]);
				}
				m.normals = normals;
			}
			/*
			if(m.bindposes != null)
			{
				var bindposes = m.bindposes;
				if(bindposes != null)
				{
					for(int i = 0; i < bindposes.Length; i++)
					{
						bindposes[i] = bindposes[i] * matrix;
					}
				}
				m.bindposes = bindposes;
				Debug.Log("Bindposes fixed for " + m.name);
			}
			*/

			/*
			for(int i = 0; i < tangents.Length; i++)
			{
				tangents[i] = matrix.MultiplyPoint3x4(tangents[i]);
			}
			*/
			//m.SetTangents(tangents);

			if(calculateTangents)
			{
				m.RecalculateTangents();
			}
			m.RecalculateBounds();
		}

		private void ApplyTransformFix(Transform t, Vector3 storedPos, Quaternion storedRot)
		{
			//Debug.Log("Fixing transform: " + t.name);
			t.position = storedPos;
			t.rotation = storedRot;

			var fix = new Vector3(-1, 1, -1);
			
			t.position = Vector3.Scale(t.position, fix);
			t.eulerAngles = Vector3.Scale(t.eulerAngles, fix);
			t.Rotate(new Vector3(-90, 0, 0), Space.Self);

			t.localScale = new Vector3(t.localScale.x, t.localScale.z, t.localScale.y);
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
