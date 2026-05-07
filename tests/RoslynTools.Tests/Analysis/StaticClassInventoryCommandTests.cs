using RoslynTools.Services;

namespace RoslynTools.Tests.Analysis;

public class StaticClassInventoryCommandTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Fixture helpers
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    ///     LeafService — static class called only from one InstanceClass (not from any static class).
    ///     Expected: migrationDifficulty = "leaf"
    /// </summary>
    private static AdhocProjectBuilder BuildLeafFixture()
    {
        var proj = new AdhocProjectBuilder("Inventory");

        proj.AddSource("LeafService.cs", """
                                         namespace Inventory;
                                         public static class LeafService
                                         {
                                             public static void DoWork() { }
                                         }
                                         """);

        proj.AddSource("InstanceCaller.cs", """
                                            namespace Inventory;
                                            public class InstanceCaller
                                            {
                                                public void Run() => LeafService.DoWork();
                                            }
                                            """);

        return proj;
    }

    /// <summary>
    ///     MidService — static class called from exactly 2 static classes.
    ///     Expected: migrationDifficulty = "mid"
    /// </summary>
    private static AdhocProjectBuilder BuildMidFixture()
    {
        var proj = new AdhocProjectBuilder("Inventory");

        proj.AddSource("MidService.cs", """
                                        namespace Inventory;
                                        public static class MidService
                                        {
                                            public static void DoWork() { }
                                        }
                                        """);

        proj.AddSource("StaticCallerA.cs", """
                                           namespace Inventory;
                                           public static class StaticCallerA
                                           {
                                               public static void Run() => MidService.DoWork();
                                           }
                                           """);

        proj.AddSource("StaticCallerB.cs", """
                                           namespace Inventory;
                                           public static class StaticCallerB
                                           {
                                               public static void Run() => MidService.DoWork();
                                           }
                                           """);

        return proj;
    }

    /// <summary>
    ///     HubService — static class called from 4 static classes.
    ///     Expected: migrationDifficulty = "hub"
    /// </summary>
    private static AdhocProjectBuilder BuildHubFixture()
    {
        var proj = new AdhocProjectBuilder("Inventory");

        proj.AddSource("HubService.cs", """
                                        namespace Inventory;
                                        public static class HubService
                                        {
                                            public static void DoWork() { }
                                        }
                                        """);

        proj.AddSource("S1.cs",
            "namespace Inventory; public static class S1 { public static void R() => HubService.DoWork(); }");
        proj.AddSource("S2.cs",
            "namespace Inventory; public static class S2 { public static void R() => HubService.DoWork(); }");
        proj.AddSource("S3.cs",
            "namespace Inventory; public static class S3 { public static void R() => HubService.DoWork(); }");
        proj.AddSource("S4.cs",
            "namespace Inventory; public static class S4 { public static void R() => HubService.DoWork(); }");

        return proj;
    }

    /// <summary>
    ///     Isolated static class — no callers at all.
    ///     Expected: migrationDifficulty = "leaf" (zero static callers → leaf)
    /// </summary>
    private static AdhocProjectBuilder BuildIsolatedFixture()
    {
        var proj = new AdhocProjectBuilder("Inventory");

        proj.AddSource("IsolatedService.cs", """
                                             namespace Inventory;
                                             public static class IsolatedService
                                             {
                                                 public static void DoWork() { }
                                             }
                                             """);

        return proj;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 1. leaf classification
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Leaf_static_class_called_only_from_instance_is_classified_leaf()
    {
        using var proj = BuildLeafFixture();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var report = await new StaticClassInventoryService().AnalyzeAsync(sln);

            var leaf = report.Classes.Single(c => c.Fqn.EndsWith("LeafService", StringComparison.Ordinal));
            Assert.Equal("leaf", leaf.MigrationDifficulty);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. mid classification
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Mid_static_class_called_from_two_static_classes_is_classified_mid()
    {
        using var proj = BuildMidFixture();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var report = await new StaticClassInventoryService().AnalyzeAsync(sln);

            var mid = report.Classes.Single(c => c.Fqn.EndsWith("MidService", StringComparison.Ordinal));
            Assert.Equal("mid", mid.MigrationDifficulty);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. hub classification
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hub_static_class_called_from_four_static_classes_is_classified_hub()
    {
        using var proj = BuildHubFixture();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var report = await new StaticClassInventoryService().AnalyzeAsync(sln);

            var hub = report.Classes.Single(c => c.Fqn.EndsWith("HubService", StringComparison.Ordinal));
            Assert.Equal("hub", hub.MigrationDifficulty);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. isolated (zero callers) is also leaf
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Isolated_static_class_with_no_callers_is_classified_leaf()
    {
        using var proj = BuildIsolatedFixture();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var report = await new StaticClassInventoryService().AnalyzeAsync(sln);

            var isolated = report.Classes.Single(c => c.Fqn.EndsWith("IsolatedService", StringComparison.Ordinal));
            Assert.Equal("leaf", isolated.MigrationDifficulty);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. caller lists are populated correctly
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CalledFromInstanceClasses_contains_instance_caller_fqn()
    {
        using var proj = BuildLeafFixture();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var report = await new StaticClassInventoryService().AnalyzeAsync(sln);

            var leaf = report.Classes.Single(c => c.Fqn.EndsWith("LeafService", StringComparison.Ordinal));
            Assert.Contains(leaf.CalledFromInstanceClasses,
                fqn => fqn.EndsWith("InstanceCaller", StringComparison.Ordinal));
            Assert.Empty(leaf.CalledFromStaticClasses);
        }
    }

    [Fact]
    public async Task CalledFromStaticClasses_contains_static_caller_fqns()
    {
        using var proj = BuildMidFixture();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var report = await new StaticClassInventoryService().AnalyzeAsync(sln);

            var mid = report.Classes.Single(c => c.Fqn.EndsWith("MidService", StringComparison.Ordinal));
            Assert.Equal(2, mid.CalledFromStaticClasses.Count);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. sort order: leaf before mid before hub
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Results_sorted_leaf_first_then_mid_then_hub()
    {
        var proj = new AdhocProjectBuilder("Mixed");

        // HubService — 4 static callers
        proj.AddSource("HubService.cs",
            "namespace Mixed; public static class HubService { public static void Do() { } }");
        proj.AddSource("H1.cs",
            "namespace Mixed; public static class H1 { public static void R() => HubService.Do(); }");
        proj.AddSource("H2.cs",
            "namespace Mixed; public static class H2 { public static void R() => HubService.Do(); }");
        proj.AddSource("H3.cs",
            "namespace Mixed; public static class H3 { public static void R() => HubService.Do(); }");
        proj.AddSource("H4.cs",
            "namespace Mixed; public static class H4 { public static void R() => HubService.Do(); }");

        // MidService — 2 static callers
        proj.AddSource("MidService.cs",
            "namespace Mixed; public static class MidService { public static void Do() { } }");
        proj.AddSource("M1.cs",
            "namespace Mixed; public static class M1 { public static void R() => MidService.Do(); }");
        proj.AddSource("M2.cs",
            "namespace Mixed; public static class M2 { public static void R() => MidService.Do(); }");

        // LeafService — called only from instance
        proj.AddSource("LeafService.cs",
            "namespace Mixed; public static class LeafService { public static void Do() { } }");
        proj.AddSource("Instance.cs",
            "namespace Mixed; public class Instance { public void R() => LeafService.Do(); }");

        using (proj)
        {
            var (workspace, sln) = proj.Build();
            using (workspace)
            {
                var report = await new StaticClassInventoryService().AnalyzeAsync(sln);
                var classList = report.Classes.ToList();

                var leaf = classList.Single(c => c.Fqn.EndsWith("LeafService", StringComparison.Ordinal));
                var mid = classList.Single(c => c.Fqn.EndsWith("MidService", StringComparison.Ordinal));
                var hub = classList.Single(c => c.Fqn.EndsWith("HubService", StringComparison.Ordinal));

                var leafIdx = classList.IndexOf(leaf);
                var midIdx = classList.IndexOf(mid);
                var hubIdx = classList.IndexOf(hub);

                Assert.True(leafIdx < midIdx, $"leaf ({leafIdx}) should come before mid ({midIdx})");
                Assert.True(midIdx < hubIdx, $"mid ({midIdx}) should come before hub ({hubIdx})");
            }
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. totalStaticClasses count
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TotalStaticClasses_matches_classes_count()
    {
        using var proj = BuildLeafFixture();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var report = await new StaticClassInventoryService().AnalyzeAsync(sln);

            Assert.Equal(report.TotalStaticClasses, report.Classes.Count);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. memberCount is populated
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MemberCount_reflects_declared_members()
    {
        var proj = new AdhocProjectBuilder("Count");
        proj.AddSource("TwoMember.cs", """
                                       namespace Count;
                                       public static class TwoMember
                                       {
                                           public static void Alpha() { }
                                           public static void Beta() { }
                                       }
                                       """);

        using (proj)
        {
            var (workspace, sln) = proj.Build();
            using (workspace)
            {
                var report = await new StaticClassInventoryService().AnalyzeAsync(sln);

                var cls = report.Classes.Single(c => c.Fqn.EndsWith("TwoMember", StringComparison.Ordinal));
                Assert.Equal(2, cls.MemberCount);
            }
        }
    }
}
