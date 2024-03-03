using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace D3TEditor.BlenderModelFixer
{
	public class BlenderModelPostProcessor : AssetPostprocessor
	{
		private class TransformCurves
		{
			public EditorCurveBinding positionX;
			public EditorCurveBinding positionY;
			public EditorCurveBinding positionZ;

			public EditorCurveBinding rotationX;
			public EditorCurveBinding rotationY;
			public EditorCurveBinding rotationZ;
			public EditorCurveBinding rotationW;

			public EditorCurveBinding scaleX;
			public EditorCurveBinding scaleY;
			public EditorCurveBinding scaleZ;
		}

		const float SQRT_2_HALF = 0.70710678f;
		private static readonly Vector3 Z_FLIP_SCALE = new Vector3(-1, 1, -1);
		private static readonly Quaternion ROTATION_FIX = new Quaternion(-SQRT_2_HALF, 0, 0, SQRT_2_HALF);
		private static readonly Quaternion ROTATION_FIX_Z_FLIP = new Quaternion(0, SQRT_2_HALF, SQRT_2_HALF, 0);
		private static readonly Quaternion ANIM_ROTATION_FIX = new Quaternion(SQRT_2_HALF, 0, 0, SQRT_2_HALF);

		void OnPostprocessModel(GameObject root)
		{
			var modelImporter = assetImporter as ModelImporter;
			if(ShouldApplyBlenderTransformFix(modelImporter, out var userData))
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

				Quaternion rotation = flipZ ? ROTATION_FIX_Z_FLIP : ROTATION_FIX;
				Matrix4x4 matrix = Matrix4x4.Rotate(rotation);
				foreach(var mesh in meshes)
				{
					ApplyMeshFix(mesh, matrix, modelImporter.importTangents != ModelImporterTangents.None);
				}

				List<Mesh> fixedSkinnedMeshes = new List<Mesh>();
				foreach(var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
				{
					ApplyBindPoseFix(smr, modifications, fixedSkinnedMeshes);
				}

				modelImporter.SaveAndReimport();
			}
		}

		void OnPostprocessAnimation(GameObject root, AnimationClip clip)
		{
			var modelImporter = assetImporter as ModelImporter;
			if(ShouldApplyBlenderTransformFix(modelImporter, out var userData))
			{
				bool flipZ = userData.GetBool(nameof(BlenderFixesExtraData.flipZAxis), false);
				var bindings = AnimationUtility.GetCurveBindings(clip);

				Dictionary<string, TransformCurves> transformCurves = new Dictionary<string, TransformCurves>();
				foreach(var binding in bindings)
				{
					if(binding.type == typeof(Transform))
					{
						if(!transformCurves.ContainsKey(binding.path)) transformCurves.Add(binding.path, new TransformCurves());
						var curves = transformCurves[binding.path];
						switch(binding.propertyName)
						{
							case "m_LocalPosition.x": curves.positionX = binding; break;
							case "m_LocalPosition.y": curves.positionY = binding; break;
							case "m_LocalPosition.z": curves.positionZ = binding; break;
							case "m_LocalRotation.x": curves.rotationX = binding; break;
							case "m_LocalRotation.y": curves.rotationY = binding; break;
							case "m_LocalRotation.z": curves.rotationZ = binding; break;
							case "m_LocalRotation.w": curves.rotationW = binding; break;
							case "m_LocalScale.x": curves.scaleX = binding; break;
							case "m_LocalScale.y": curves.scaleY = binding; break;
							case "m_LocalScale.z": curves.scaleZ = binding; break;
							default: Debug.LogError($"Unknown binding in transform animation: {binding.propertyName}"); break;
						}
					}
				}

				foreach(var kv in transformCurves)
				{
					var curves = kv.Value;
					if(curves.positionX.path != null)
					{
						//Position is animated
						var posXCurve = AnimationUtility.GetEditorCurve(clip, curves.positionX);
						var posYCurve = AnimationUtility.GetEditorCurve(clip, curves.positionY);
						var posZCurve = AnimationUtility.GetEditorCurve(clip, curves.positionZ);
						for(int i = 0; i < posXCurve.keys.Length; i++)
						{
							var time = posXCurve.keys[i].time;
							var keyX = posXCurve[i];
							var keyY = posYCurve[i];
							var keyZ = posZCurve[i];
							Vector3 pos = new Vector3(keyX.value, keyY.value, keyZ.value);
							Vector3 inTangents = new Vector3(keyX.inTangent, keyY.inTangent, keyZ.inTangent);
							Vector3 outTangents = new Vector3(keyX.outTangent, keyY.outTangent, keyZ.outTangent);
							if(flipZ)
							{
								pos = Vector3.Scale(pos, Z_FLIP_SCALE);
								inTangents = Vector3.Scale(inTangents, Z_FLIP_SCALE);
								outTangents = Vector3.Scale(outTangents, Z_FLIP_SCALE);
							}
							posXCurve.MoveKey(i, new Keyframe(time, pos.x, inTangents.x, outTangents.x));
							posYCurve.MoveKey(i, new Keyframe(time, pos.y, inTangents.y, outTangents.y));
							posZCurve.MoveKey(i, new Keyframe(time, pos.z, inTangents.z, outTangents.z));
						}
						AnimationUtility.SetEditorCurve(clip, curves.positionX, posXCurve);
						AnimationUtility.SetEditorCurve(clip, curves.positionY, posYCurve);
						AnimationUtility.SetEditorCurve(clip, curves.positionZ, posZCurve);
					}
					if(curves.rotationX.path != null)
					{
						//Rotation is animated
						var rotXCurve = AnimationUtility.GetEditorCurve(clip, curves.rotationX);
						var rotYCurve = AnimationUtility.GetEditorCurve(clip, curves.rotationY);
						var rotZCurve = AnimationUtility.GetEditorCurve(clip, curves.rotationZ);
						var rotWCurve = AnimationUtility.GetEditorCurve(clip, curves.rotationW);
						for(int i = 0; i < rotXCurve.keys.Length; i++)
						{
							var time = rotXCurve.keys[i].time;
							var keyX = rotXCurve[i];
							var keyY = rotYCurve[i];
							var keyZ = rotZCurve[i];
							var keyW = rotWCurve[i];
							Quaternion rot = new Quaternion(keyX.value, keyY.value, keyZ.value, keyW.value);
							Quaternion inTangents = new Quaternion(keyX.inTangent, keyY.inTangent, keyZ.inTangent, keyW.inTangent);
							Quaternion outTangents = new Quaternion(keyX.outTangent, keyY.outTangent, keyZ.outTangent, keyW.outTangent);
							if(flipZ)
							{
								var mirror = new Quaternion(0, 1, 0, 0);
								rot = mirror * (rot * ANIM_ROTATION_FIX) * mirror;
								inTangents = mirror * (inTangents * ANIM_ROTATION_FIX) * mirror;
								outTangents = mirror * (outTangents * ANIM_ROTATION_FIX) * mirror;
							}
							else
							{
								rot *= ANIM_ROTATION_FIX;
								inTangents *= ANIM_ROTATION_FIX;
								outTangents *= ANIM_ROTATION_FIX;
							}
							rotXCurve.MoveKey(i, new Keyframe(time, rot.x, inTangents.x, outTangents.x));
							rotYCurve.MoveKey(i, new Keyframe(time, rot.y, inTangents.y, outTangents.y));
							rotZCurve.MoveKey(i, new Keyframe(time, rot.z, inTangents.z, outTangents.z));
							rotWCurve.MoveKey(i, new Keyframe(time, rot.w, inTangents.w, outTangents.w));
						}
						AnimationUtility.SetEditorCurve(clip, curves.rotationX, rotXCurve);
						AnimationUtility.SetEditorCurve(clip, curves.rotationY, rotYCurve);
						AnimationUtility.SetEditorCurve(clip, curves.rotationZ, rotZCurve);
						AnimationUtility.SetEditorCurve(clip, curves.rotationW, rotWCurve);
					}
					if(curves.scaleX.path != null)
					{
						//Scale is animated
						var scaleXCurve = AnimationUtility.GetEditorCurve(clip, curves.scaleX);
						var scaleYCurve = AnimationUtility.GetEditorCurve(clip, curves.scaleY);
						var scaleZCurve = AnimationUtility.GetEditorCurve(clip, curves.scaleZ);
						//Just swap Y and Z curves
						AnimationUtility.SetEditorCurve(clip, curves.scaleX, scaleXCurve);
						AnimationUtility.SetEditorCurve(clip, curves.scaleY, scaleZCurve);
						AnimationUtility.SetEditorCurve(clip, curves.scaleZ, scaleYCurve);
					}
				}
				modelImporter.SaveAndReimport();
			}
		}

		private Vector4 QuaternionToVector4(Quaternion q)
		{
			return new Vector4(q.x, q.y, q.z, q.w);
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
				t.position = Vector3.Scale(t.position, Z_FLIP_SCALE);
				t.eulerAngles = Vector3.Scale(t.eulerAngles, Z_FLIP_SCALE);
			}

			float sign = flipZ ? 1 : -1;

			if((t.localEulerAngles - new Vector3(89.98f * sign, 0, 0)).magnitude < 0.001f)
			{
				//Reset local rotation
				t.localRotation = Quaternion.identity;
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
