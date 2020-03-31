﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Microsoft.DotNet.PlatformAbstractions;
using Microsoft.NET.TestFramework;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.Commands;
using Microsoft.NET.TestFramework.ProjectConstruction;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.NET.Publish.Tests
{
    public class PublishItemsOutputGroupTests : SdkTest
    {
        public PublishItemsOutputGroupTests(ITestOutputHelper log) : base(log)
        {
        }

        private readonly static List<string> FrameworkAssemblies = new List<string>()
        {
            "api-ms-win-core-console-l1-1-0.dll",
            "System.Runtime.dll",
            "WindowsBase.dll",
        };

        [Fact]
        public void WithRid()
        {
            this.RunPublishItemsOutputGroupTest(specifyRid: true, singleFile: false);
        }

        [Fact]
        public void NoRid()
        {
            this.RunPublishItemsOutputGroupTest(specifyRid: false, singleFile: false);
        }

        [Fact]
        public void SingleFile()
        {
            this.RunPublishItemsOutputGroupTest(specifyRid: true, singleFile: true);
        }

        [Fact]
        public void GroupBuildsWithoutPublish()
        {
            var testProject = this.SetupProject();
            var testAsset = _testAssetsManager.CreateTestProject(testProject);

            var restoreCommand = new RestoreCommand(Log, testAsset.Path, testProject.Name);
            restoreCommand
                .Execute()
                .Should()
                .Pass();

            var buildCommand = new BuildCommand(Log, testAsset.Path, testProject.Name);
            buildCommand
                .Execute("/p:RuntimeIdentifier=win-x86;DesignTimeBuild=true", "/t:PublishItemsOutputGroup")
                .Should()
                .Pass();

            // Confirm we were able to build the output group without the publish actually happening
            var publishDir = new DirectoryInfo(Path.Combine(buildCommand.GetOutputDirectory(testProject.TargetFrameworks).FullName, "win-x86", "publish"));
            publishDir
                .Should()
                .NotExist();
        }

        private void RunPublishItemsOutputGroupTest(bool specifyRid, bool singleFile)
        {
            var testProject = this.SetupProject(specifyRid, singleFile);
            var testAsset = _testAssetsManager.CreateTestProject(testProject);

            var restoreCommand = new RestoreCommand(Log, testAsset.Path, testProject.Name);
            restoreCommand
                .Execute()
                .Should()
                .Pass();

            var command = new GetValuesCommand(
                Log,
                Path.Combine(testAsset.Path, testProject.Name),
                testProject.TargetFrameworks,
                "PublishItemsOutputGroupOutputs",
                GetValuesCommand.ValueType.Item)
            {
                DependsOnTargets = "PublishItemsOutputGroup",
                MetadataNames = { "TargetPath", "IsKeyOutput" },
            };

            command.Execute().Should().Pass();
            var items = from item in command.GetValuesWithMetadata()
                        select new
                        {
                            Identity = item.value,
                            TargetPath = item.metadata["TargetPath"],
                            IsKeyOutput = item.metadata["IsKeyOutput"]
                        };

            Log.WriteLine("PublishItemsOutputGroup contains '{0}' items:", items.Count());
            foreach (var item in items)
            {
                Log.WriteLine("    '{0}': TargetPath = '{1}', IsKeyOutput = '{2}'", item.Identity, item.TargetPath, item.IsKeyOutput);
            }

            if (RuntimeEnvironment.OperatingSystemPlatform != Platform.Darwin)
            {
                // Check that there's only one key item, and it's the exe
                items.
                    Where(i => i.IsKeyOutput.Equals("true", StringComparison.OrdinalIgnoreCase)).
                    Should().
                    ContainSingle(i => i.TargetPath.Equals($"{testProject.Name}.exe", StringComparison.OrdinalIgnoreCase));
            }

            // Framework assemblies should be there if we specified and rid and this isn't in the single file case
            if (specifyRid && !singleFile)
            {
                FrameworkAssemblies.ForEach(fa => items.Should().ContainSingle(i => i.TargetPath.Equals(fa, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                FrameworkAssemblies.ForEach(fa => items.Should().NotContain(i => i.TargetPath.Equals(fa, StringComparison.OrdinalIgnoreCase)));
            }

            // The deps.json file should be included unless this is the single file case
            if (!singleFile)
            {
                items.Should().ContainSingle(i => i.TargetPath.Equals($"{testProject.Name}.deps.json", StringComparison.OrdinalIgnoreCase));
            }
        }

        private TestProject SetupProject(bool specifyRid = true, bool singleFile = false)
        {
            var testProject = new TestProject()
            {
                Name = "TestPublishOutputGroup",
                TargetFrameworks = "netcoreapp3.0",
                IsSdkProject = true,
                IsExe = true
            };

            testProject.AdditionalProperties["RuntimeIdentifiers"] = "win-x86";

            // Use a test-specific packages folder
            testProject.AdditionalProperties["RestorePackagesPath"] = @"$(MSBuildProjectDirectory)\..\pkg";

            // This target is primarily used during design time builds
            testProject.AdditionalProperties["DesignTimeBuild"] = "true";

            if (specifyRid)
            {
                testProject.AdditionalProperties["RuntimeIdentifier"] = "win-x86";
            }

            if (singleFile)
            {
                testProject.AdditionalProperties["PublishSingleFile"] = "true";
            }

            return testProject;
        }
    }
}
