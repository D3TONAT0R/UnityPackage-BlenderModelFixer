using System.Collections.Generic;
using Unity.Plastic.Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace D3TEditor.BlenderModelFixer
{
	public class AssetUserData
	{
		public Dictionary<string, object> data;

		public bool IsDirty { get; private set; }

		public AssetUserData()
		{
			data = new Dictionary<string, object>();
		}

		private AssetUserData(Dictionary<string, object> data)
		{
			this.data = data;
		}

		public bool ContainsKey(string key) => data.ContainsKey(key);

		public bool GetBool(string key, bool fallback)
		{
			if(data.TryGetValue(key, out object value)) return System.Convert.ToBoolean(value);
			else return fallback;
		}

		public string GetString(string key, string fallback)
		{
			if(data.TryGetValue(key, out object value)) return System.Convert.ToString(value);
			else return fallback;
		}

		public int GetInt(string key, int fallback)
		{
			if(data.TryGetValue(key, out object value)) return System.Convert.ToInt32(value);
			else return fallback;
		}

		public float GetFloat(string key, float fallback)
		{
			if(data.TryGetValue(key, out object value)) return System.Convert.ToSingle(value);
			else return fallback;
		}

		public void SetValue(string key, object value)
		{
			if(data.ContainsKey(key) && data[key] == value)
			{
				return;
			}
			data[key] = value;
			IsDirty = true;
		}

		public void SetValue(SerializedProperty property)
		{
			SetValue(property.name, GetPropertyValue(property));
		}

		private static object GetPropertyValue(SerializedProperty property)
		{
			switch(property.propertyType)
			{
				case SerializedPropertyType.Boolean: return property.boolValue;
				case SerializedPropertyType.Integer: return property.intValue;
				case SerializedPropertyType.Float: return property.floatValue;
				case SerializedPropertyType.String: return property.stringValue;
				default: throw new System.NotImplementedException();
			}
		}

		public string Serialize()
		{
			IsDirty = false;
			return JsonConvert.SerializeObject(data);
		}

		public bool ApplyModified(Object asset)
		{
			if(!IsDirty) return false;
			return ApplyModified(AssetDatabase.GetAssetPath(asset));
		}

		public bool ApplyModified(string assetPath)
		{
			if(!IsDirty) return false;
			AssetImporter.GetAtPath(assetPath).userData = Serialize();
			return true;
		}

		public static AssetUserData Get(Object asset)
		{
			return Get(AssetDatabase.GetAssetPath(asset));
		}

		public static AssetUserData Get(string assetPath)
		{
			return Deserialize(AssetImporter.GetAtPath(assetPath).userData);
		}

		public static AssetUserData Deserialize(string userDataString)
		{
			if(string.IsNullOrWhiteSpace(userDataString))
			{
				return new AssetUserData();
			}
			else
			{
				return new AssetUserData(JsonConvert.DeserializeObject<Dictionary<string, object>>(userDataString));
			}
		}
	}
}
