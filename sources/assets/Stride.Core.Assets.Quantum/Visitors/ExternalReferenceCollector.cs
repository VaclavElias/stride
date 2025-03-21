// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Quantum;

namespace Stride.Core.Assets.Quantum.Visitors;

/// <summary>
/// A visitor that will collect all object references that target objects that are not included in the visited object.
/// </summary>
public class ExternalReferenceCollector : IdentifiableObjectVisitorBase
{
    private readonly AssetPropertyGraphDefinition propertyGraphDefinition;

    private readonly HashSet<IIdentifiable> internalReferences = [];
    private readonly HashSet<IIdentifiable> externalReferences = [];
    private readonly Dictionary<IIdentifiable, List<NodeAccessor>> externalReferenceAccessors = [];

    private ExternalReferenceCollector(AssetPropertyGraphDefinition propertyGraphDefinition)
        : base(propertyGraphDefinition)
    {
        this.propertyGraphDefinition = propertyGraphDefinition;
    }

    /// <summary>
    /// Computes the external references to the given root node.
    /// </summary>
    /// <param name="propertyGraphDefinition">The property graph definition to use to analyze the graph.</param>
    /// <param name="root">The root node to analyze.</param>
    /// <returns>A set containing all external references to identifiable objects.</returns>
    public static HashSet<IIdentifiable> GetExternalReferences(AssetPropertyGraphDefinition propertyGraphDefinition, IGraphNode root)
    {
        var visitor = new ExternalReferenceCollector(propertyGraphDefinition);
        visitor.Visit(root);
        // An IIdentifiable can have been recorded both as internal and external reference. In this case we still want to clone it so let's remove it from external references
        visitor.externalReferences.ExceptWith(visitor.internalReferences);
        return visitor.externalReferences;
    }

    /// <summary>
    /// Computes the external references to the given root node and their accessors.
    /// </summary>
    /// <param name="propertyGraphDefinition">The property graph definition to use to analyze the graph.</param>
    /// <param name="root">The root node to analyze.</param>
    /// <returns>A set containing all external references to identifiable objects.</returns>
    public static Dictionary<IIdentifiable, List<NodeAccessor>> GetExternalReferenceAccessors(AssetPropertyGraphDefinition propertyGraphDefinition, IGraphNode root)
    {
        var visitor = new ExternalReferenceCollector(propertyGraphDefinition);
        visitor.Visit(root);
        // An IIdentifiable can have been recorded both as internal and external reference. In this case we still want to clone it so let's remove it from external references
        foreach (var internalReference in visitor.internalReferences)
        {
            visitor.externalReferenceAccessors.Remove(internalReference);
        }
        return visitor.externalReferenceAccessors;
    }

    protected override void ProcessIdentifiableMembers(IIdentifiable identifiable, IMemberNode member)
    {
        if (propertyGraphDefinition.IsMemberTargetObjectReference(member, identifiable))
        {
            externalReferences.Add(identifiable);
            if (!externalReferenceAccessors.TryGetValue(identifiable, out var accessors))
            {
                externalReferenceAccessors.Add(identifiable, accessors = []);
            }
            accessors.Add(CurrentPath.GetAccessor());
        }
        else
        {
            internalReferences.Add(identifiable);
        }
    }

    protected override void ProcessIdentifiableItems(IIdentifiable identifiable, IObjectNode collection, NodeIndex index)
    {
        if (propertyGraphDefinition.IsTargetItemObjectReference(collection, index, identifiable))
        {
            externalReferences.Add(identifiable);
            if (!externalReferenceAccessors.TryGetValue(identifiable, out var accessors))
            {
                externalReferenceAccessors.Add(identifiable, accessors = []);
            }
            accessors.Add(CurrentPath.GetAccessor());
        }
        else
        {
            internalReferences.Add(identifiable);
        }
    }
}
