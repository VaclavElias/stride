// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using Stride.Core.Quantum.References;
using Stride.Core.Reflection;

namespace Stride.Core.Quantum;

/// <summary>
/// The default <see cref="INodeBuilder"/> implementation that construct a graph from a data object.
/// </summary>
internal class DefaultNodeBuilder : DataVisitorBase, INodeBuilder
{
    private readonly Stack<IInitializingGraphNode> contextStack = new();
    private readonly HashSet<IGraphNode> referenceContents = [];
    private readonly List<Type> primitiveTypes = [];
    private static readonly Type[] InternalPrimitiveTypes = [typeof(decimal), typeof(string), typeof(Guid)];
    private IInitializingObjectNode? rootNode;
    private Guid rootGuid;

    public DefaultNodeBuilder(NodeContainer nodeContainer)
    {
        NodeContainer = nodeContainer;
        primitiveTypes.AddRange(InternalPrimitiveTypes);
    }

    /// <inheritdoc/>
    public NodeContainer NodeContainer { get; }

    /// <inheritdoc/>
    public INodeFactory NodeFactory { get; set; } = new DefaultNodeFactory();

    /// <summary>
    /// Reset the visitor in order to use it to generate another model.
    /// </summary>
    public override void Reset()
    {
        rootNode = null;
        rootGuid = Guid.Empty;
        contextStack.Clear();
        referenceContents.Clear();
        base.Reset();
    }

    public void RegisterPrimitiveType(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || primitiveTypes.Contains(type))
            return;

        primitiveTypes.Add(type);
    }

    public void UnregisterPrimitiveType(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || InternalPrimitiveTypes.Contains(type))
            throw new InvalidOperationException("The given type cannot be unregistered from the list of primitive types");

        primitiveTypes.Remove(type);
    }

    public bool IsPrimitiveType([NotNullWhen(true)] Type? type)
    {
        if (type == null)
            return false;

        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType != null)
            type = underlyingType;

        return type.IsPrimitive || type.IsEnum || primitiveTypes.Any(x => x.IsAssignableFrom(type));
    }

    /// <inheritdoc/>
    public IObjectNode Build(object obj, Guid guid)
    {
        ArgumentNullException.ThrowIfNull(obj);
        Reset();
        rootGuid = guid;
        var typeDescriptor = TypeDescriptorFactory.Find(obj.GetType());
        VisitObject(obj, (ObjectDescriptor)typeDescriptor!, true);
        return rootNode!;
    }

    /// <inheritdoc/>
    public override void VisitObject(object obj, ObjectDescriptor descriptor, bool visitMembers)
    {
        ITypeDescriptor currentDescriptor = descriptor;

        bool isRootNode = contextStack.Count == 0;
        if (isRootNode)
        {
            // If we're visiting a value type as "object" we need to use a special "boxed" node.
            var content = descriptor.Type.IsValueType ? NodeFactory.CreateBoxedNode(this, rootGuid, obj, descriptor)
                : NodeFactory.CreateObjectNode(this, rootGuid, obj, descriptor);

            currentDescriptor = content.Descriptor;
            rootNode = (IInitializingObjectNode)content;
            if (content.IsReference && currentDescriptor.Type.IsStruct())
                throw new QuantumConsistencyException("A collection type", "A structure type", rootNode);

            if (content.IsReference)
                referenceContents.Add(content);

            PushContextNode(rootNode);
        }

        if (!IsPrimitiveType(currentDescriptor.Type))
        {
            base.VisitObject(obj, descriptor, true);
        }

        if (isRootNode)
        {
            PopContextNode();
            rootNode!.Seal();
        }
    }

    /// <inheritdoc/>
    public override void VisitCollection(IEnumerable collection, CollectionDescriptor descriptor)
    {
        if (!descriptor.HasIndexerAccessors)
            throw new NotSupportedException("Collections that do not have indexer accessors are not supported in Quantum.");

        // Don't visit items unless they are primitive or enumerable (collections within collections)
        if (IsCollection(descriptor.ElementType))
        {
            base.VisitCollection(collection, descriptor);
        }
    }

    /// <inheritdoc/>
    public override void VisitDictionary(object dictionary, DictionaryDescriptor descriptor)
    {
        if (!IsPrimitiveType(descriptor.KeyType))
            throw new InvalidOperationException("The type of dictionary key must be a primary type.");

        // Don't visit items unless they are primitive or enumerable (collections within collections)
        if (IsCollection(descriptor.ValueType))
        {
            base.VisitDictionary(dictionary, descriptor);
        }
    }

    /// <inheritdoc/>
    public override void VisitObjectMember(object container, ObjectDescriptor containerDescriptor, IMemberDescriptor member, object? value)
    {
        // If this member should contains a reference, create it now.
        var containerNode = (IInitializingObjectNode)GetContextNode();
        var guid = Guid.NewGuid();
        var content = (MemberNode)NodeFactory.CreateMemberNode(this, guid, containerNode, member, value);
        containerNode.AddMember(content);

        if (content.IsReference)
            referenceContents.Add(content);

        PushContextNode(content);
        if (content.TargetReference == null)
        {
            // For enumerable references, we visit the member to allow VisitList or VisitDictionary or VisitSet or VisitCollection to enrich correctly the node.
            Visit(content.Retrieve());
        }
        PopContextNode();

        content.Seal();
    }

    public IReference? CreateReferenceForNode(Type type, object? value, bool isMember)
    {
        if (isMember)
        {
            return !IsPrimitiveType(type) ? Reference.CreateReference(value, type, NodeIndex.Empty, true) : null;
        }

        var descriptor = TypeDescriptorFactory.Find(value?.GetType());
        if (descriptor is CollectionDescriptor || descriptor is DictionaryDescriptor)
        {
            var valueType = GetElementValueType(descriptor);
            return !IsPrimitiveType(valueType) ? Reference.CreateReference(value, type, NodeIndex.Empty, false) : null;
        }

        return null;
    }

    private void PushContextNode(IInitializingGraphNode node)
    {
        contextStack.Push(node);
    }

    private void PopContextNode()
    {
        contextStack.Pop();
    }

    private IInitializingGraphNode GetContextNode()
    {
        return contextStack.Peek();
    }

    private static bool IsCollection(Type type)
    {
        return typeof(ICollection).IsAssignableFrom(type);
    }

    private static Type? GetElementValueType(ITypeDescriptor descriptor)
    {
        var collectionDescriptor = descriptor as CollectionDescriptor;
        return descriptor is DictionaryDescriptor dictionaryDescriptor ? dictionaryDescriptor.ValueType : collectionDescriptor?.ElementType;
    }
}
