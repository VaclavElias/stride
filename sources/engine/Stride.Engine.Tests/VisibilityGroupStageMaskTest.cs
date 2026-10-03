// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;

using Xunit;

using Stride.Core.Mathematics;
using Stride.Rendering;

namespace Stride.Engine.Tests;

/// <summary>
/// <see cref="VisibilityGroup"/> tests a render object against a view by matching stage masks: one for the
/// stages the view renders, one per object for the stages its selectors activated. Both are rebuilt in place,
/// so a rebuild that only adds bits lets stale ones through. These tests need no graphics device.
/// </summary>
/// <remarks>
/// A mask holds 32 stages per word (<see cref="VisibilityGroup.RenderStageMaskSizePerEntry"/>). Each test runs with
/// its two stages in the first word, split across the first and second, and both in the second, by registering
/// unused stages ahead of them.
/// </remarks>
public class VisibilityGroupStageMaskTest
{
    // Stage A at index 0, 31 and 40; stage B right after it
    public static TheoryData<int> LeadingStageCounts => new() { 0, 31, 40 };

    private sealed class TestObject : RenderObject { }

    private sealed class TestFeature : RootRenderFeature
    {
        public override Type SupportedRenderObjectType => typeof(TestObject);
    }

    /// <summary>
    /// The view mask is one array shared by every view the group collects. A second view must see only its own stages,
    /// not the union with the view collected before it.
    /// </summary>
    [Theory]
    [MemberData(nameof(LeadingStageCounts))]
    public void ViewCollectsOnlyObjectsOfItsOwnStages(int leadingStages)
    {
        var (system, stageA, stageB, _, _) = CreateSystem(leadingStages);
        using var group = new VisibilityGroup(system);

        group.RenderObjects.Add(new TestObject { RenderGroup = RenderGroup.Group0 });
        group.RenderObjects.Add(new TestObject { RenderGroup = RenderGroup.Group1 });

        var viewA = CreateView(stageA);
        var viewB = CreateView(stageB);
        group.TryCollect(viewA);
        group.TryCollect(viewB);

        Assert.Single(viewA.RenderObjects);
        Assert.Single(viewB.RenderObjects);
    }

    /// <summary>
    /// An object's mask is rebuilt when selectors change. A stage its selectors no longer activate must leave the mask,
    /// or the object keeps being collected into views of that stage.
    /// </summary>
    [Theory]
    [MemberData(nameof(LeadingStageCounts))]
    public void ObjectLeavesStageWhenItsSelectorIsRemoved(int leadingStages)
    {
        var (system, stageA, _, feature, stageASelector) = CreateSystem(leadingStages);
        using var group = new VisibilityGroup(system);

        var renderObject = new TestObject { RenderGroup = RenderGroup.Group0 };
        group.RenderObjects.Add(renderObject);

        Assert.Equal(1, Collect(group, stageA));

        feature.RenderStageSelectors.Remove(stageASelector);

        Assert.Equal(0, Collect(group, stageA));
        Assert.False(renderObject.ActiveRenderStages[stageA.Index].Active);
    }

    /// <summary>
    /// A render system with stages A and B, registered after <paramref name="leadingStages"/> unused ones,
    /// and a feature that renders Group0 in stage A and Group1 in stage B.
    /// </summary>
    private static (RenderSystem System, RenderStage StageA, RenderStage StageB, TestFeature Feature, RenderStageSelector StageASelector) CreateSystem(int leadingStages)
    {
        var system = new RenderSystem();

        for (int i = 0; i < leadingStages; i++)
            system.RenderStages.Add(new RenderStage($"Unused{i}", "Unused"));

        var stageA = new RenderStage("A", "A");
        var stageB = new RenderStage("B", "B");
        system.RenderStages.Add(stageA);
        system.RenderStages.Add(stageB);

        var stageASelector = new SimpleGroupToRenderStageSelector { RenderGroup = RenderGroupMask.Group0, RenderStage = stageA, EffectName = "Test" };
        var stageBSelector = new SimpleGroupToRenderStageSelector { RenderGroup = RenderGroupMask.Group1, RenderStage = stageB, EffectName = "Test" };

        var feature = new TestFeature();
        feature.RenderStageSelectors.Add(stageASelector);
        feature.RenderStageSelectors.Add(stageBSelector);
        system.RenderFeatures.Add(feature);

        return (system, stageA, stageB, feature, stageASelector);
    }

    private static RenderView CreateView(RenderStage stage)
    {
        var view = new RenderView
        {
            View = Matrix.Identity,
            Projection = Matrix.Identity,
            ViewProjection = Matrix.Identity,
            CullingMode = CameraCullingMode.None,
            CullingMask = RenderGroupMask.All,
            NearClipPlane = 0.1f,
            FarClipPlane = 100f,
        };
        view.RenderStages.Add(new RenderViewStage(stage));
        return view;
    }

    /// <summary>
    /// Collects a fresh view of one stage and returns how many objects it gathered.
    /// </summary>
    private static int Collect(VisibilityGroup group, RenderStage stage)
    {
        var view = CreateView(stage);
        group.TryCollect(view);
        return view.RenderObjects.Count;
    }
}
