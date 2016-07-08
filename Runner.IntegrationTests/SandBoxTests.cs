﻿// Copyright 2015 ThoughtWorks, Inc.
//
// This file is part of Gauge-CSharp.
//
// Gauge-CSharp is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// Gauge-CSharp is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with Gauge-CSharp.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using Gauge.CSharp.Lib;
using NUnit.Framework;
using Gauge.CSharp.Lib.Attribute;
using Gauge.CSharp.Runner.Models;
using Gauge.CSharp.Runner.Processors;
using Gauge.Messages;
using Google.ProtocolBuffers;

namespace Gauge.CSharp.Runner.IntegrationTests
{
    [TestFixture]
    public class SandboxTests
    {		
		private readonly string _testProjectPath = TestUtils.GetIntegrationTestSampleDirectory();

        [SetUp]
        public void Setup()
        {
            Environment.SetEnvironmentVariable("GAUGE_PROJECT_ROOT", _testProjectPath);
        }

        private static void AssertRunnerDomainDidNotLoadUsersAssembly ()
		{
			Assert.AreNotEqual ("0.0.0", FileVersionInfo.GetVersionInfo (typeof(AfterScenario).Assembly.Location).ProductVersion,
				"Runner's test domain should not load the Gauge.CSharp.Lib assembly with 0.0.0 version");
			// 0.0.0 version should be only loaded in sandbox. 
			// Runner should have its own version, the one we just built in this project
		}

        [Test]
        public void ShouldLoadTargetLibAssemblyInSandbox()
        {
            var sandbox = SandboxFactory.Create();

            // The sample project uses a special version of Gauge Lib, versioned 0.0.0 for testing.
            // The actual Gauge CSharp runner uses a different version of Lib 
			// used by sample project
			Assert.AreEqual("0.0.0",sandbox.TargetLibAssemblyVersion);
			// used by runner
			AssertRunnerDomainDidNotLoadUsersAssembly ();
        }

		[Test]
		public void ShouldNotLoadTargetLibAssemblyInRunnersDomain()
		{
			SandboxFactory.Create();

			// The sample project uses a special version of Gauge Lib, versioned 0.0.0 for testing.
			// The actual Gauge CSharp runner uses a different version of Lib 
			// used by runner
			AssertRunnerDomainDidNotLoadUsersAssembly ();
		}

        [Test]
        public void ShouldGetAllStepMethods()
        {
            var sandbox = SandboxFactory.Create();
			AssertRunnerDomainDidNotLoadUsersAssembly ();
            var stepMethods = sandbox.GetStepMethods();

            Assert.AreEqual(9, stepMethods.Count);
        }

        [Test]
        public void ShouldGetAllStepTexts()
        {
            var sandbox = SandboxFactory.Create();
            var stepTexts = sandbox.GetAllStepTexts().ToList();

            new List<string>
            {
                "Say <what> to <who>",
                "A context step which gets executed before every scenario",
                "Step that takes a table <table>",
                "Refactoring Say <what> to <who>",
                "Refactoring A context step which gets executed before every scenario",
                "Refactoring Step that takes a table <table>"
            }.ForEach(s => Assert.Contains(s, stepTexts));
        }

        [Test]
        public void ShouldExecuteMethodAndReturnResult()
        {
            var sandbox = SandboxFactory.Create();
            var stepMethods = sandbox.GetStepMethods();
			AssertRunnerDomainDidNotLoadUsersAssembly ();
            var methodInfo = stepMethods.First(info => string.CompareOrdinal(info.Name, "IntegrationTestSample.StepImplementation.Context") == 0);

            var executionResult = sandbox.ExecuteMethod(methodInfo);
            Assert.True(executionResult.Success);
        }

        [Test]
        public void SuccessIsFalseOnUnserializableExceptionThrown()
        {
            const string expectedMessage = "I am a custom exception";
            var sandbox = SandboxFactory.Create();
            var stepMethods = sandbox.GetStepMethods();
			AssertRunnerDomainDidNotLoadUsersAssembly ();
            var methodInfo = stepMethods.First(info => string.CompareOrdinal(info.Name, "IntegrationTestSample.StepImplementation.ThrowUnserializableException") == 0);

            var executionResult = sandbox.ExecuteMethod(methodInfo);
            Assert.False(executionResult.Success);
            Assert.AreEqual(expectedMessage, executionResult.ExceptionMessage);
			StringAssert.Contains("IntegrationTestSample.StepImplementation.ThrowUnserializableException",executionResult.StackTrace);
        }

        [Test]
        public void SuccessIsFalseOnSerializableExceptionThrown()
        {
            const string expectedMessage = "I am a custom serializable exception";
            var sandbox = SandboxFactory.Create();
            var stepMethods = sandbox.GetStepMethods();
            var methodInfo = stepMethods.First(info => string.CompareOrdinal(info.Name, "IntegrationTestSample.StepImplementation.ThrowSerializableException") == 0);

            var executionResult = sandbox.ExecuteMethod(methodInfo);

            Assert.False(executionResult.Success);
            Assert.AreEqual(expectedMessage, executionResult.ExceptionMessage);
			StringAssert.Contains("IntegrationTestSample.StepImplementation.ThrowSerializableException",executionResult.StackTrace);
        }

        [Test]
        public void ShouldCreateTableFromTargetType()
        {
            var sandbox = SandboxFactory.Create();
            var stepMethods = sandbox.GetStepMethods();
            var methodInfo = stepMethods.First(info => string.CompareOrdinal(info.Name, "IntegrationTestSample.StepImplementation.ReadTable") == 0);

            var table = new Table(new List<string> {"foo", "bar"});
            table.AddRow(new List<string> {"foorow1", "barrow1"});
            table.AddRow(new List<string> {"foorow2", "barrow2"});
            
            var executionResult = sandbox.ExecuteMethod(methodInfo, SerializeTable(table));
            Console.WriteLine("Success: {0},\nException: {1},\nStackTrace :{2},\nSource : {3}",
                executionResult.Success, executionResult.ExceptionMessage, executionResult.StackTrace, executionResult.Source);
            Assert.True(executionResult.Success);
        }

        [Test]
        public void ShouldExecuteMethodFromRequest()
        {
            const string parameterizedStepText = "Step that takes a table {}";
            const string stepText = "Step that takes a table <table>";
            var sandbox = SandboxFactory.Create();
            var gaugeMethod = sandbox.GetStepMethods()
                .First(method => method.Name == "IntegrationTestSample.StepImplementation.ReadTable");
            var scannedSteps = new List<KeyValuePair<string, GaugeMethod>> {new KeyValuePair<string, GaugeMethod>(parameterizedStepText, gaugeMethod)};
            var aliases = new Dictionary<string, bool> {{parameterizedStepText, false}};
            var stepTextMap = new Dictionary<string, string> { {parameterizedStepText, stepText}};
            var stepRegistry = new StepRegistry(scannedSteps, stepTextMap, aliases);

            var executeStepProcessor = new ExecuteStepProcessor(stepRegistry, new MethodExecutor(sandbox));

            var builder = Message.CreateBuilder();
            var protoTable = ProtoTable.CreateBuilder()
                .SetHeaders(
                    ProtoTableRow.CreateBuilder().AddRangeCells(new List<string> {"foo", "bar"}))
                .AddRangeRows(new List<ProtoTableRow>
                {
                    ProtoTableRow.CreateBuilder()
                        .AddRangeCells(new List<string> {"foorow1", "foorow2"})
                        .Build()
                }).Build();
            var message = builder
                .SetMessageId(1234)
                .SetMessageType(Message.Types.MessageType.ExecuteStep)
                .SetExecuteStepRequest(
                    ExecuteStepRequest.CreateBuilder()
                        .SetParsedStepText(parameterizedStepText)
                        .SetActualStepText(stepText)
                        .AddParameters(
                            Parameter.CreateBuilder()
                                .SetName("table")
                                .SetParameterType(Parameter.Types.ParameterType.Table)
                                .SetTable(protoTable).Build()
                        ).Build()
                ).Build();
            var result = executeStepProcessor.Process(message);

            AssertRunnerDomainDidNotLoadUsersAssembly();
            var protoExecutionResult = result.ExecutionStatusResponse.ExecutionResult;
            Assert.IsNotNull(protoExecutionResult);
            Assert.IsFalse(protoExecutionResult.Failed);
        }

        [Test]
        public void ShouldExecuteHooks() 
        { }

        [Test]
        public void ShouldExecuteDatastoreInit() { }

        [Test]
        public void ShouldGetStepTextsForMethod() { }

        [Test]
        public void ShouldGetPendingMessages() { }

        [Test]
        public void ShouldCaptureScreenshotOnFailure() { }

        [TearDown]
        public void TearDown()
        {
            Environment.SetEnvironmentVariable("GAUGE_PROJECT_ROOT", null);
			AssertRunnerDomainDidNotLoadUsersAssembly ();
        }

        private static string SerializeTable(Table table)
        {
            var serializer = new DataContractJsonSerializer(typeof(Table));
            using (var memoryStream = new MemoryStream())
            {
                serializer.WriteObject(memoryStream, table);
                return Encoding.UTF8.GetString(memoryStream.ToArray());
            }            
        }
    }
}
