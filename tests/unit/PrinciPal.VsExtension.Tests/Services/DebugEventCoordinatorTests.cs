using System.Collections.Generic;
using System.Threading.Tasks;
using NSubstitute;
using PrinciPal.Common.Abstractions;
using PrinciPal.Common.Errors.Extension;
using PrinciPal.Common.Results;
using PrinciPal.Domain.ValueObjects;
using PrinciPal.VsExtension.Abstractions;
using PrinciPal.VsExtension.Services;
using Xunit;

namespace PrinciPal.VsExtension.Tests.Services
{
    public class DebugEventCoordinatorTests
    {
        private readonly IDebuggerReader _reader;
        private readonly IDebugStatePublisher _publisher;
        private readonly IExtensionLogger _logger;
        private readonly DebugEventCoordinator _sut;

        public DebugEventCoordinatorTests()
        {
            _reader = Substitute.For<IDebuggerReader>();
            _publisher = Substitute.For<IDebugStatePublisher>();
            _logger = Substitute.For<IExtensionLogger>();
            _sut = new DebugEventCoordinator(_reader, _publisher, _logger);
        }

        [Fact]
        public void ShouldPushState_WhenDebuggingVS_ReturnsFalse()
        {
            _reader.IsDebuggingVisualStudio().Returns(true);

            var result = _sut.ShouldPushState();

            Assert.False(result);
        }

        [Fact]
        public void ShouldPushState_WhenNotDebuggingVS_ReturnsTrue()
        {
            _reader.IsDebuggingVisualStudio().Returns(false);

            var result = _sut.ShouldPushState();

            Assert.True(result);
        }

        [Fact]
        public void BuildDebugState_WhenNotInBreakMode_ReturnsEmptyState()
        {
            _reader.IsInBreakMode.Returns(false);

            var state = _sut.BuildDebugState();

            Assert.False(state.IsInBreakMode);
            Assert.Null(state.CurrentLocation);
            Assert.Empty(state.Locals);
            Assert.Empty(state.CallStack);
            Assert.Empty(state.Breakpoints);

            // Verify readers were NOT called
            _reader.DidNotReceive().ReadCurrentLocation();
            _reader.DidNotReceive().ReadLocals(Arg.Any<int>());
            _reader.DidNotReceive().ReadCallStack(Arg.Any<int>());
            _reader.DidNotReceive().ReadBreakpoints();
        }

        [Fact]
        public void BuildDebugState_WhenInBreakMode_PopulatesAllFields()
        {
            _reader.IsInBreakMode.Returns(true);

            var expectedLocation = new SourceLocation
            {
                FilePath = "Test.cs",
                Line = 42,
                FunctionName = "DoWork"
            };
            var expectedLocals = new List<LocalVariable>
            {
                new LocalVariable { Name = "x", Value = "1", Type = "int" }
            };
            var expectedStack = new List<StackFrameInfo>
            {
                new StackFrameInfo { Index = 1, FunctionName = "DoWork" }
            };
            var expectedBreakpoints = new List<BreakpointInfo>
            {
                new BreakpointInfo { FilePath = "Test.cs", Line = 42 }
            };

            _reader.ReadCurrentLocation().Returns(Result.Success(expectedLocation));
            _reader.ReadLocals(Arg.Any<int>()).Returns(Result.Success(expectedLocals));
            _reader.ReadCallStack(Arg.Any<int>()).Returns(Result.Success(expectedStack));
            _reader.ReadBreakpoints().Returns(Result.Success(expectedBreakpoints));

            var state = _sut.BuildDebugState();

            Assert.True(state.IsInBreakMode);
            Assert.Same(expectedLocation, state.CurrentLocation);
            Assert.Same(expectedLocals, state.Locals);
            Assert.Same(expectedStack, state.CallStack);
            Assert.Same(expectedBreakpoints, state.Breakpoints);
        }

        [Fact]
        public void BuildDebugState_WhenReaderFails_ReturnsPartialStateAndLogs()
        {
            _reader.IsInBreakMode.Returns(true);

            var locationError = new ComReadError("location", "COM failed");
            _reader.ReadCurrentLocation().Returns(Result.Failure<SourceLocation>(locationError));

            var expectedLocals = new List<LocalVariable>
            {
                new LocalVariable { Name = "y", Value = "2", Type = "int" }
            };
            _reader.ReadLocals(Arg.Any<int>()).Returns(Result.Success(expectedLocals));

            var stackError = new ComReadError("call stack", "Thread gone");
            _reader.ReadCallStack(Arg.Any<int>()).Returns(Result.Failure<List<StackFrameInfo>>(stackError));

            var expectedBreakpoints = new List<BreakpointInfo>();
            _reader.ReadBreakpoints().Returns(Result.Success(expectedBreakpoints));

            var state = _sut.BuildDebugState();

            Assert.True(state.IsInBreakMode);
            Assert.Null(state.CurrentLocation);
            Assert.Same(expectedLocals, state.Locals);
            Assert.Empty(state.CallStack); // default empty list, not populated
            Assert.Same(expectedBreakpoints, state.Breakpoints);

            // Verify failures were logged
            _logger.Received(1).Log(Arg.Is<string>(s => s.Contains("location")));
            _logger.Received(1).Log(Arg.Is<string>(s => s.Contains("call stack")));
        }

        [Fact]
        public async Task PublishStateAsync_DelegatesToPublisher()
        {
            var state = new DebugState { IsInBreakMode = true };
            _publisher.PushDebugStateAsync(state).Returns(Task.FromResult(Result.Success()));

            var result = await _sut.PublishStateAsync(state);

            Assert.True(result.IsSuccess);
            await _publisher.Received(1).PushDebugStateAsync(state);
        }

        [Fact]
        public async Task ClearStateAsync_DelegatesToPublisher()
        {
            _publisher.ClearDebugStateAsync().Returns(Task.FromResult(Result.Success()));

            var result = await _sut.ClearStateAsync();

            Assert.True(result.IsSuccess);
            await _publisher.Received(1).ClearDebugStateAsync();
        }
    }
}
