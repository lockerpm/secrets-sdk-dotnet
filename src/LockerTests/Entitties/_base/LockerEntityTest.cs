using Locker;
using Xunit;

namespace LockerTests
{
    public class LockerEntityTest
    {
        public LockerEntityTest()
        {
        }

        [Fact]
        public void FromJsonAuto()
        {
            var json = "{\"id\": \"123\", \"object\": \"secret\"}";
            var o = LockerEntity.FromJson(json);
            Assert.NotNull(o);
            Assert.IsType<Secret>(o);
            Assert.Equal("123", actual: ((Secret)o).Id);
        }

        [Fact]
        public void FromJsonAutoUnknownObject()
        {
            var json = "{\"id\": \"123\", \"object\": \"foo\"}";
            var o = LockerEntity.FromJson(json);
            Assert.Null(o);
        }

        [Fact]
        public void FromJsonAutoNoObject()
        {
            var json = "{\"id\": \"123\"}";

            var o = LockerEntity.FromJson(json);

            Assert.Null(o);
        }
    }
}