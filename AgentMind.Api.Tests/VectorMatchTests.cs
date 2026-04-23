using System;
using System.Collections.Generic;
using AgentMind.Api.Interfaces;
using Xunit;

namespace AgentMind.Api.Tests
{
    public class VectorMatchTests
    {
        [Fact]
        public void VectorMatch_HoldsValues()
        {
            var payload = new Dictionary<string, object> { { "content", "hello" } };
            var id = Guid.NewGuid();
            var vm = new VectorMatch(id, 0.9, payload);

            Assert.Equal(id, vm.Id);
            Assert.Equal(0.9, vm.Score);
            Assert.True(vm.Payload.ContainsKey("content"));
            Assert.Equal("hello", vm.Payload["content"].ToString());
        }
    }
}
