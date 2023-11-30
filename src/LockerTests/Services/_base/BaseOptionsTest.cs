using Newtonsoft.Json;

namespace LockerTests
{
    using Xunit;
    using Locker;


    public class BaseOptionsTest : BaseLockerTest
    {
        [Fact]
        public void SerializeAndDeserializeExpandedAndExtraParams()
        {
            var options = new BaseOptions();
            options.AddExpand("expand_me");
            options.AddExtraParam("foo", "String!");
            options.AddExtraParam("bar", 1L);
            var json = JsonConvert.SerializeObject(options);
            var deserialized = JsonConvert.DeserializeObject<BaseOptions>(json);

            Assert.Equal(options.Expand, deserialized.Expand);
            Assert.True(options.ExtraParams.Count == deserialized.ExtraParams.Count);
            Assert.All(
                deserialized.ExtraParams,
                pair => Assert.Equal(options.ExtraParams[pair.Key], deserialized.ExtraParams[pair.Key]));
        }
    }
}