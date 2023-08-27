using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace D3TEditor.BlenderModelFixer
{
	public class BlenderFBXPostProcessor : AssetPostprocessor
	{

		void OnPostprocessModel(GameObject root)
		{
			var mi = assetImporter as ModelImporter;
			if(ShouldApplyBlenderTransformFix(mi, out var userData))
			{
				bool flipZ = userData.GetBool(nameof(BlenderFixesExtraData.flipZAxis), false);

				//Debug.Log("Applying fix on "+root.name);
				List<Mesh> meshes = new List<Mesh>();
				LocateMeshes(meshes, root.transform);

				var transformStore = new Dictionary<Transform, (Vector3, Quaternion)>();
				foreach(var t in root.GetComponentsInChildren<Transform>(true))
				{
					if(t == root.transform) continue;
					transformStore.Add(t, (t.position, t.rotation));
				}

				var modifications = new Dictionary<Transform, Matrix4x4>();
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
						var mod = ApplyTransformFix(t, stored.Item1, stored.Item2, flipZ);
						modifications.Add(t, mod);
					}
				}

				//Matrix4x4 matrix = Matrix4x4.Rotate(Quaternion.Euler(-89.98f, flipZ ? 180 : 0, 0));
				Matrix4x4 matrix = Matrix4x4.Rotate(Quaternion.Euler(-90, flipZ ? 180 : 0, 0));
				foreach(var mesh in meshes)
				{
					ApplyMeshFix(mesh, matrix, mi.importTangents != ModelImporterTangents.None);
				}

				List<Mesh> fixedSkinnedMeshes = new List<Mesh>();
				foreach(var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
				{
					ApplyBindPoseFix(smr, modifications, fixedSkinnedMeshes);
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
			//Debug.Log("Fixing mesh: " + m.name);
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

		private Matrix4x4 ApplyTransformFix(Transform t, Vector3 storedPos, Quaternion storedRot, bool flipZ)
		{
			Matrix4x4 before = t.localToWorldMatrix;

			//Debug.Log("Fixing transform: " + t.name);
			t.position = storedPos;
			t.rotation = storedRot;

			if(flipZ)
			{
				var fix = new Vector3(-1, 1, -1);
				t.position = Vector3.Scale(t.position, fix);
				t.eulerAngles = Vector3.Scale(t.eulerAngles, fix);
			}

			float sign = flipZ ? 1 : -1;

			if((t.localEulerAngles - new Vector3(89.98f * sign, 0, 0)).sqrMagnitude < 0.001f)
			{
				t.Rotate(new Vector3(-89.98f * sign, 0, 0), Space.Self);
			}
			else
			{
				t.Rotate(new Vector3(-90 * sign, 0, 0), Space.Self);
			}

			t.localScale = new Vector3(t.localScale.x, t.localScale.z, t.localScale.y);

			Matrix4x4 after = t.localToWorldMatrix;

			return after * before.inverse;
		}

		private void ApplyBindPoseFix(SkinnedMeshRenderer skinnedMeshRenderer, Dictionary<Transform, Matrix4x4> transformations, List<Mesh> fixedMeshes)
		{
			var m = skinnedMeshRenderer.sharedMesh;

			if(fixedMeshes.Contains(m)) return;

			fixedMeshes.Add(m);

			if(m.bindposes != null)
			{
				var bindposes = m.bindposes;
				if(bindposes != null)
				{
					for(int i = 0; i < bindposes.Length; i++)
					{
						var fix = transformations[skinnedMeshRenderer.bones[i]];
						bindposes[i] *= fix.inverse;
					}
				}
				m.bindposes = bindposes;
				//Debug.Log("Bindposes fixed for " + m.name);
			}
		}

		private bool ShouldApplyBlenderTransformFix(ModelImporter mi, out AssetUserData userData)
		{
			if(IsModelValidForFix(mi.assetPath))
			{
				userData = AssetUserData.Deserialize(mi.userData);
				return userData.GetBool(nameof(BlenderFixesExtraData.applyAxisConversion), false);
			}
			else
			{
				userData = null;
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
