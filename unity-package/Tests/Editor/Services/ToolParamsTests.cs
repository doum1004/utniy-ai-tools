using NUnit.Framework;
using UnityAITools.Editor.Services;

namespace UnityAITools.Editor.Tests.Services
{
    [TestFixture]
    public class ToolParamsTests
    {
        [Test]
        public void GetString_ReturnsValue()
        {
            var p = new ToolParams("{\"name\":\"test\"}");
            Assert.AreEqual("test", p.GetString("name"));
        }

        [Test]
        public void GetString_ReturnsFallback_WhenMissing()
        {
            var p = new ToolParams("{}");
            Assert.AreEqual("default", p.GetString("name", "default"));
        }

        [Test]
        public void GetString_ReturnsNull_WhenMissing_NoFallback()
        {
            var p = new ToolParams("{}");
            Assert.IsNull(p.GetString("name"));
        }

        [Test]
        public void RequireString_ThrowsWhenMissing()
        {
            var p = new ToolParams("{}");
            Assert.Throws<System.ArgumentException>(() => p.RequireString("name"));
        }

        [Test]
        public void RequireString_ReturnsValue()
        {
            var p = new ToolParams("{\"name\":\"hello\"}");
            Assert.AreEqual("hello", p.RequireString("name"));
        }

        [Test]
        public void GetInt_ReturnsIntegerFromLong()
        {
            var p = new ToolParams("{\"count\":42}");
            Assert.AreEqual(42, p.GetInt("count"));
        }

        [Test]
        public void GetInt_ReturnsNull_WhenMissing()
        {
            var p = new ToolParams("{}");
            Assert.IsNull(p.GetInt("count"));
        }

        [Test]
        public void GetFloat_ReturnsFloatFromDouble()
        {
            var p = new ToolParams("{\"speed\":3.14}");
            Assert.AreEqual(3.14f, p.GetFloat("speed"), 0.01f);
        }

        [Test]
        public void GetFloat_ReturnsNull_WhenMissing()
        {
            var p = new ToolParams("{}");
            Assert.IsNull(p.GetFloat("speed"));
        }

        [Test]
        public void GetBool_ReturnsBool()
        {
            var p = new ToolParams("{\"active\":true}");
            Assert.AreEqual(true, p.GetBool("active"));
        }

        [Test]
        public void GetBool_ReturnsFalse()
        {
            var p = new ToolParams("{\"active\":false}");
            Assert.AreEqual(false, p.GetBool("active"));
        }

        [Test]
        public void GetBool_ReturnsNull_WhenMissing()
        {
            var p = new ToolParams("{}");
            Assert.IsNull(p.GetBool("active"));
        }

        [Test]
        public void GetVector3_ReturnsArray()
        {
            var p = new ToolParams("{\"pos\":[1.0,2.0,3.0]}");
            var v = p.GetVector3("pos");
            Assert.IsNotNull(v);
            Assert.AreEqual(3, v.Length);
            Assert.AreEqual(1.0f, v[0], 0.01f);
            Assert.AreEqual(2.0f, v[1], 0.01f);
            Assert.AreEqual(3.0f, v[2], 0.01f);
        }

        [Test]
        public void GetVector3_ReturnsNull_WhenMissing()
        {
            var p = new ToolParams("{}");
            Assert.IsNull(p.GetVector3("pos"));
        }

        [Test]
        public void GetUnityVector3_ReturnsVector3()
        {
            var p = new ToolParams("{\"pos\":[1.0,2.0,3.0]}");
            var v = p.GetUnityVector3("pos");
            Assert.IsNotNull(v);
            Assert.AreEqual(1.0f, v.Value.x, 0.01f);
            Assert.AreEqual(2.0f, v.Value.y, 0.01f);
            Assert.AreEqual(3.0f, v.Value.z, 0.01f);
        }

        [Test]
        public void GetStringList_ReturnsList()
        {
            var p = new ToolParams("{\"tags\":[\"a\",\"b\",\"c\"]}");
            var list = p.GetStringList("tags");
            Assert.IsNotNull(list);
            Assert.AreEqual(3, list.Count);
            Assert.AreEqual("a", list[0]);
            Assert.AreEqual("b", list[1]);
            Assert.AreEqual("c", list[2]);
        }

        [Test]
        public void GetStringList_ReturnsNull_WhenMissing()
        {
            var p = new ToolParams("{}");
            Assert.IsNull(p.GetStringList("tags"));
        }

        [Test]
        public void HasKey_ReturnsTrueForExistingKey()
        {
            var p = new ToolParams("{\"key\":\"value\"}");
            Assert.IsTrue(p.HasKey("key"));
        }

        [Test]
        public void HasKey_ReturnsFalseForMissingKey()
        {
            var p = new ToolParams("{}");
            Assert.IsFalse(p.HasKey("key"));
        }

        [Test]
        public void GetRaw_ReturnsRawValue()
        {
            var p = new ToolParams("{\"key\":\"value\"}");
            Assert.AreEqual("value", p.GetRaw("key"));
        }

        [Test]
        public void GetRaw_ReturnsNull_WhenMissing()
        {
            var p = new ToolParams("{}");
            Assert.IsNull(p.GetRaw("key"));
        }

        [Test]
        public void Constructor_HandlesNullJson()
        {
            var p = new ToolParams(null);
            Assert.IsNull(p.GetString("key"));
        }

        [Test]
        public void Constructor_HandlesEmptyJson()
        {
            var p = new ToolParams("");
            Assert.IsNull(p.GetString("key"));
        }
    }

    [TestFixture]
    public class MiniJsonTests
    {
        [Test]
        public void Deserialize_Object()
        {
            var result = MiniJson.Deserialize("{\"key\":\"value\"}") as System.Collections.Generic.Dictionary<string, object>;
            Assert.IsNotNull(result);
            Assert.AreEqual("value", result["key"]);
        }

        [Test]
        public void Deserialize_NestedObject()
        {
            var result = MiniJson.Deserialize("{\"outer\":{\"inner\":\"value\"}}") as System.Collections.Generic.Dictionary<string, object>;
            Assert.IsNotNull(result);
            var inner = result["outer"] as System.Collections.Generic.Dictionary<string, object>;
            Assert.IsNotNull(inner);
            Assert.AreEqual("value", inner["inner"]);
        }

        [Test]
        public void Deserialize_Array()
        {
            var result = MiniJson.Deserialize("[1,2,3]") as System.Collections.Generic.List<object>;
            Assert.IsNotNull(result);
            Assert.AreEqual(3, result.Count);
        }

        [Test]
        public void Deserialize_StringWithEscapes()
        {
            var result = MiniJson.Deserialize("{\"msg\":\"hello\\nworld\"}") as System.Collections.Generic.Dictionary<string, object>;
            Assert.AreEqual("hello\nworld", result["msg"]);
        }

        [Test]
        public void Deserialize_Boolean()
        {
            var result = MiniJson.Deserialize("{\"a\":true,\"b\":false}") as System.Collections.Generic.Dictionary<string, object>;
            Assert.AreEqual(true, result["a"]);
            Assert.AreEqual(false, result["b"]);
        }

        [Test]
        public void Deserialize_Null()
        {
            var result = MiniJson.Deserialize("{\"a\":null}") as System.Collections.Generic.Dictionary<string, object>;
            Assert.IsNull(result["a"]);
        }

        [Test]
        public void Deserialize_Numbers()
        {
            var result = MiniJson.Deserialize("{\"int\":42,\"float\":3.14}") as System.Collections.Generic.Dictionary<string, object>;
            Assert.AreEqual(42L, result["int"]);
            Assert.AreEqual(3.14, (double)result["float"], 0.001);
        }

        [Test]
        public void Deserialize_NullInput()
        {
            Assert.IsNull(MiniJson.Deserialize(null));
        }

        [Test]
        public void Deserialize_EmptyInput()
        {
            Assert.IsNull(MiniJson.Deserialize(""));
        }

        [Test]
        public void Serialize_Object()
        {
            var dict = new System.Collections.Generic.Dictionary<string, object>
            {
                { "key", "value" },
                { "num", 42 }
            };
            var json = MiniJson.Serialize(dict);
            Assert.IsTrue(json.Contains("\"key\":\"value\""));
            Assert.IsTrue(json.Contains("\"num\":42"));
        }

        [Test]
        public void Serialize_Array()
        {
            var list = new System.Collections.Generic.List<object> { 1, 2, 3 };
            var json = MiniJson.Serialize(list);
            Assert.AreEqual("[1,2,3]", json);
        }

        [Test]
        public void Serialize_String()
        {
            Assert.AreEqual("\"hello\"", MiniJson.Serialize("hello"));
        }

        [Test]
        public void Serialize_StringWithEscapes()
        {
            var json = MiniJson.Serialize("hello\nworld");
            Assert.AreEqual("\"hello\\nworld\"", json);
        }

        [Test]
        public void Serialize_Bool()
        {
            Assert.AreEqual("true", MiniJson.Serialize(true));
            Assert.AreEqual("false", MiniJson.Serialize(false));
        }

        [Test]
        public void Serialize_Null()
        {
            Assert.AreEqual("null", MiniJson.Serialize(null));
        }

        [Test]
        public void Roundtrip_ComplexObject()
        {
            var original = "{\"name\":\"test\",\"count\":5,\"active\":true,\"tags\":[\"a\",\"b\"],\"nested\":{\"x\":1.5}}";
            var deserialized = MiniJson.Deserialize(original);
            var serialized = MiniJson.Serialize(deserialized);
            var reDeserialized = MiniJson.Deserialize(serialized) as System.Collections.Generic.Dictionary<string, object>;

            Assert.AreEqual("test", reDeserialized["name"]);
            Assert.AreEqual(5L, reDeserialized["count"]);
            Assert.AreEqual(true, reDeserialized["active"]);
        }

        [Test]
        public void Deserialize_NegativeNumber()
        {
            var result = MiniJson.Deserialize("{\"val\":-42}") as System.Collections.Generic.Dictionary<string, object>;
            Assert.AreEqual(-42L, result["val"]);
        }

        [Test]
        public void Deserialize_ScientificNotation()
        {
            var result = MiniJson.Deserialize("{\"val\":1.5e2}") as System.Collections.Generic.Dictionary<string, object>;
            Assert.AreEqual(150.0, (double)result["val"], 0.001);
        }
    }
}
