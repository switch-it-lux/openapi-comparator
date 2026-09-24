// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using Criteo.OpenApi.Comparator.Comparators.Extensions;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace Criteo.OpenApi.Comparator.UTest
{
    [TestFixture]
    public class PrimitiveTypesExtensionTests
    {
        [Test]
        public void DifferFrom_ShouldReturn_false_When_SameString()
        {
            const string oldString = "string";
            const string newString = "string";
            Assert.That(oldString.DifferFrom(newString), Is.False);
        }

        [Test]
        public void DifferFrom_ShouldReturn_false_When_NullString()
        {
            const string oldString = null;
            const string newString = null;
            Assert.That(oldString.DifferFrom(newString), Is.False);
        }

        [Test]
        public void DifferFrom_ShouldReturn_true_When_NullOldString()
        {
            const string oldString = null;
            const string newString = "string";
            Assert.That(oldString.DifferFrom(newString), Is.True);
        }

        [Test]
        public void DifferFrom_ShouldReturn_true_When_NullNewString()
        {
            const string oldString = "string";
            const string newString = null;
            Assert.That(oldString.DifferFrom(newString), Is.True);
        }

        [Test]
        public void DifferFrom_ShouldReturn_false_When_SameInteger()
        {
            int? oldInteger = 8;
            int? newInteger = 8;
            Assert.That(oldInteger.DifferFrom(newInteger), Is.False);
        }

        [Test]
        public void DifferFrom_ShouldReturn_false_When_NullInteger()
        {
            int? oldInteger = null;
            int? newInteger = null;
            Assert.That(oldInteger.DifferFrom(newInteger), Is.False);
        }

        [Test]
        public void DifferFrom_ShouldReturn_true_When_NullOldInteger()
        {
            int? oldInteger = null;
            int? newInteger = 8;
            Assert.That(oldInteger.DifferFrom(newInteger), Is.True);
        }

        [Test]
        public void DifferFrom_ShouldReturn_true_When_NullNewInteger()
        {
            int? oldInteger = 8;
            Assert.That(oldInteger.DifferFrom(null), Is.True);
        }

        [Test]
        public void DifferFrom_ShouldReturn_false_When_SameJsonInteger()
        {
            var oldInteger = JsonValue.Create(8);
            var newInteger = JsonValue.Create(8);
            Assert.That(oldInteger.DifferFrom(newInteger), Is.False);
        }

        [Test]
        public void DifferFrom_ShouldReturn_true_When_DifferentJsonInteger()
        {
            var oldInteger = JsonValue.Create(8);
            var newInteger = JsonValue.Create(1);
            Assert.That(oldInteger.DifferFrom(newInteger), Is.True);
        }

        [Test]
        public void DifferFrom_ShouldReturn_false_When_SameJsonBoolean()
        {
            var oldBoolean = JsonValue.Create(false);
            var newBoolean = JsonValue.Create(false);
            Assert.That(oldBoolean.DifferFrom(newBoolean), Is.False);
        }

        [Test]
        public void DifferFrom_ShouldReturn_true_When_DifferentJsonBoolean()
        {
            var oldBoolean = JsonValue.Create(false);
            var newBoolean = JsonValue.Create(true);
            Assert.That(oldBoolean.DifferFrom(newBoolean), Is.True);
        }

        [Test]
        public void DifferFrom_ShouldReturn_false_When_SameJsonString()
        {
            var oldString = JsonValue.Create("string");
            var newString = JsonValue.Create("string");
            Assert.That(oldString.DifferFrom(newString), Is.False);
        }

        [Test]
        public void DifferFrom_ShouldReturn_true_When_DifferentJsonString()
        {
            var oldString = JsonValue.Create("oldString");
            var newString = JsonValue.Create("newString");
            Assert.That(oldString.DifferFrom(newString), Is.True);
        }

        [Test]
        public void DifferFrom_ShouldReturn_false_When_SameJsonNumberWithDifferentRepresentation()
        {
            var oldNumber = JsonNode.Parse("1.50");
            var newNumber = JsonNode.Parse("1.5");
            Assert.That(oldNumber.DifferFrom(newNumber), Is.False);
        }

        [Test]
        public void DifferFrom_ShouldReturn_true_When_DifferentJsonNumber()
        {
            var oldNumber = JsonNode.Parse("1.5");
            var newNumber = JsonNode.Parse("2");
            Assert.That(oldNumber.DifferFrom(newNumber), Is.True);
        }

        [Test]
        public void DifferFrom_ShouldReturn_true_When_DifferentJsonKind()
        {
            var oldValue = JsonValue.Create("8");
            var newValue = JsonValue.Create(8);
            Assert.That(oldValue.DifferFrom(newValue), Is.True);
        }

        [TestCase("{\"a\": 1, \"b\": [1, \"x\", null]}", "{\"b\": [1.0, \"x\", null], \"a\": 1}", false)]
        [TestCase("{\"a\": 1}", "{\"a\": 2}", true)]
        [TestCase("{\"a\": 1}", "{\"a\": 1, \"b\": 2}", true)]
        [TestCase("{\"a\": null}", "{\"a\": 1}", true)]
        [TestCase("{\"a\": null}", "{\"b\": null}", true)]
        [TestCase("{\"a\": {\"b\": true}}", "{\"a\": {\"b\": false}}", true)]
        [TestCase("[1, 2]", "[2, 1]", true)]
        [TestCase("[1, 2]", "[1, 2, 3]", true)]
        [TestCase("[]", "[]", false)]
        [TestCase("[1]", "{\"0\": 1}", true)]
        public void DifferFrom_Should_Compare_Objects_And_Arrays_Deeply(string oldJson, string newJson, bool expected)
        {
            Assert.That(JsonNode.Parse(oldJson).DifferFrom(JsonNode.Parse(newJson)), Is.EqualTo(expected));
        }

        [TestCase("5", 5)]
        [TestCase("-1.5", -1.5)]
        [TestCase("1E+2", 100)]
        [TestCase("1e-3", 0.001)]
        public void ToDecimal_Should_Parse_Invariant_Number(string value, double expected)
        {
            Assert.That(value.ToDecimal(), Is.EqualTo((decimal)expected));
        }

        [TestCase("1.7976931348623157E+308")]
        [TestCase("1E+29")]
        public void ToDecimal_Should_Clamp_To_MaxValue_When_Too_Large(string value)
        {
            Assert.That(value.ToDecimal(), Is.EqualTo(decimal.MaxValue));
        }

        [TestCase("-1.7976931348623157E+308")]
        [TestCase("-1E+29")]
        public void ToDecimal_Should_Clamp_To_MinValue_When_Too_Small(string value)
        {
            Assert.That(value.ToDecimal(), Is.EqualTo(decimal.MinValue));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("abc")]
        [TestCase("NaN")]
        [TestCase("1,5")]
        public void ToDecimal_Should_Return_Null_When_Not_A_Number(string value)
        {
            Assert.That(value.ToDecimal(), Is.Null);
        }
    }
}
