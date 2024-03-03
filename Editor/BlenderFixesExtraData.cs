using UnityEditor;
using UnityEngine;

namespace D3TEditor.BlenderModelFixer
{
	public class BlenderFixesExtraData : ScriptableObject
	{
		public bool applyAxisConversion = false;
		public bool flipZAxis = true;
		public bool fixLights = true;
		public float lightIntensityFactor = 0.01f;
		public float lightRangeFactor = 0.1f;

		public void Initialize(AssetUserData userData)
		{
			var serializedObj = new SerializedObject(this);
			var prop = serializedObj.GetIterator();
			prop.Next(true);
			while(prop.Next(false))
			{
				InitField(userData, prop);
			}
			serializedObj.ApplyModifiedPropertiesWithoutUndo();
		}

		private bool InitField(AssetUserData userData, SerializedProperty property)
		{
			string propName = property.name;
			if(userData.ContainsKey(propName))
			{
				switch(property.propertyType)
				{
					case SerializedPropertyType.Boolean: 
						property.boolValue = userData.GetBool(propName, default);
						break;
					case SerializedPropertyType.Integer:
						property.intValue = userData.GetInt(propName, default);
						break;
					case SerializedPropertyType.Float:
						property.floatValue = userData.GetFloat(propName, default);
						break;
					case SerializedPropertyType.String:
						property.stringValue = userData.GetString(propName, default);
						break;
					default:
						throw new System.NotImplementedException();
				}
				return true;
			}
			return false;
		}
	} 
}
