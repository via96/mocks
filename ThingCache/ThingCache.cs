using System.Collections.Generic;
using FakeItEasy;
using NUnit.Framework;

namespace MockFramework
{
    public class ThingCache
    {
        private readonly IDictionary<string, Thing> dictionary
            = new Dictionary<string, Thing>();
        private readonly IThingService thingService;

        public ThingCache(IThingService thingService)
        {
            this.thingService = thingService;
        }

        public Thing Get(string thingId)
        {
            Thing thing;
            if (dictionary.TryGetValue(thingId, out thing))
                return thing;
            if (thingService.TryRead(thingId, out thing))
            {
                dictionary[thingId] = thing;
                return thing;
            }
            return null;
        }
    }

    [TestFixture]
    public class ThingCache_Should
    {
        private IThingService thingService;
        private ThingCache thingCache;

        private const string thingId1 = "TheDress";
        private Thing thing1 = new Thing(thingId1);

        private const string thingId2 = "CoolBoots";
        private Thing thing2 = new Thing(thingId2);

        [SetUp]
        public void SetUp()
        {
            thingService = A.Fake<IThingService>();
            thingCache = new ThingCache(thingService);
        }

        [Test]
        public void SingleThingGetting()
        {
            A.CallTo(() => thingService.TryRead(thingId1, out thing1)).Returns(true);
            Assert.AreEqual(thing1, thingCache.Get(thingId1));
            
            Thing value;
            A.CallTo(() => thingService.TryRead(A<string>.Ignored, out value)).MustHaveHappened();
        }

        [Test]
        public void MultipleThingGetting()
        {
            A.CallTo(() => thingService.TryRead(thingId1, out thing1)).Returns(true);
            A.CallTo(() => thingService.TryRead(thingId2, out thing2)).Returns(true);

            Assert.AreEqual(thing1, thingCache.Get(thingId1));
            Assert.AreEqual(thing1, thingCache.Get(thingId1));
            Assert.AreEqual(thing1, thingCache.Get(thingId1));
            Assert.AreEqual(thing1, thingCache.Get(thingId1));

            Assert.AreEqual(thing2, thingCache.Get(thingId2));
            Assert.AreEqual(thing2, thingCache.Get(thingId2));
            Assert.AreEqual(thing2, thingCache.Get(thingId2));

            Thing value;
            A.CallTo(() => thingService.TryRead(A<string>.Ignored, out value)).MustHaveHappened(2, Times.Exactly);
        }

        [Test]
        public void GetNonExistThing()
        {
            var imagineThing = new Thing("ImagineThing");
            A.CallTo(() => thingService.TryRead(A<string>.Ignored, out imagineThing)).Returns(false);
            A.CallTo(() => thingService.TryRead(thingId1, out thing1)).Returns(true);

            Assert.AreEqual(null, thingCache.Get("NonExistThing"));
            
            Thing value;
            A.CallTo(() => thingService.TryRead(A<string>.Ignored, out value)).MustHaveHappened();
        }
    }

}