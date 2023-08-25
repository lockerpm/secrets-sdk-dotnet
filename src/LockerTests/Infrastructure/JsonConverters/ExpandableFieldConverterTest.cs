using Locker.Infrastructure;

namespace LockerTests
{
    using Locker;
    using Newtonsoft.Json;
    using Xunit;

    public class ExpandableFieldConverterTest : BaseLockerTest
    {
        [Fact]
        public void DeserializeNotExpanded()
        {
            var json = "{\n  \"nested\": \"id_not_expanded\"\n}";
            var obj = JsonConvert.DeserializeObject<TestTopLevelObject>(json);
            Assert.Equal("id_not_expanded", obj.NestedId);
            Assert.Null(obj.Nested);
        }

        [Fact]
        public void DeserializeExpanded()
        {
            var json = "{\n  \"nested\": {\n    \"id\": \"id_expanded\",\n    \"bar\": 1\n  }\n}";
            var obj = JsonConvert.DeserializeObject<TestTopLevelObject>(json);
            Assert.Equal("id_expanded", obj.NestedId);
            Assert.NotNull(obj.Nested);
            Assert.Equal("id_expanded", obj.Nested.Id);
            Assert.Equal(1, obj.Nested.Bar);
        }

        [Fact]
        public void DeserializeNull()
        {
            var json = "{\n  \"nested\": null\n}";
            var obj = JsonConvert.DeserializeObject<TestTopLevelObject>(json);
            Assert.Null(obj.NestedId);
            Assert.Null(obj.Nested);
        }

        [Fact]
        public void SerializeNotExpanded()
        {
            var obj = new TestTopLevelObject()
            {
                InternalNested = new ExpandableField<TestNestedObject>()
                {
                    Id = "id_not_expanded",
                    ExpandedObject = null
                }
            };
            var expected = "{\n  \"nested\": \"id_not_expanded\"\n}";
            Assert.Equal(expected, obj.ToJson().Replace("\r\n", "\n"));
        }

        [Fact]
        public void SerializeExpanded()
        {
            var nested = new TestNestedObject
            {
                Id = "id_expanded",
                Bar = 1
            };
            var obj = new TestTopLevelObject()
            {
                InternalNested = new ExpandableField<TestNestedObject>
                {
                    Id = nested.Id,
                    ExpandedObject = nested
                }
            };
            var expected = "{\n  \"nested\": {\n    \"id\": \"id_expanded\",\n    \"bar\": 1\n  }\n}";
            Assert.Equal(expected, obj.ToJson().Replace("\r\n", "\n"));
        }

        [Fact]
        public void SerializeNull()
        {
            var obj = new TestTopLevelObject
            {
                InternalNested = new ExpandableField<TestNestedObject>
                {
                    Id = null,
                    ExpandedObject = null
                }
            };
            var expected = "{\n  \"nested\": null\n}";
            Assert.Equal(expected, obj.ToJson().Replace("\r\n", "\n"));
        }

        private class TestNestedObject : LockerEntity<TestNestedObject>, IHasId
        {
            [JsonProperty("id")] public string Id { get; set; }
            [JsonProperty("bar")] public int Bar { get; set; }
        }

        private class TestTopLevelObject : LockerEntity<TestTopLevelObject>
        {
            [JsonIgnore] public string NestedId => this.InternalNested.Id;
            [JsonIgnore] public TestNestedObject Nested => this.InternalNested.ExpandedObject;

            [JsonProperty("nested")]
            [JsonConverter(typeof(ExpandableFieldConverter<TestNestedObject>))]
            internal ExpandableField<TestNestedObject> InternalNested { get; set; }
        }
    }
}