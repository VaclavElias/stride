// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Stride.Core.Diagnostics;
using Stride.Core.Reflection;
using Stride.Core.Yaml.Serialization;
using Stride.Core.Yaml.Serialization.Serializers;

namespace Stride.Core.Yaml;

/// <summary>
/// Default Yaml serializer used to serialize assets by default.
/// </summary>
public class YamlSerializer : YamlSerializerBase
{
    private Serializer? globalSerializer;

    public static YamlSerializer Default { get; set; } = new YamlSerializer();

    public static T Load<T>(string filePath, ILogger? log = null)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(filePath);
#else
        if (filePath is null) throw new ArgumentNullException(nameof(filePath));
#endif
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (T)Default.Deserialize(stream);
    }

    /// <summary>
    /// Deserializes an object from the specified stream (expecting a YAML string).
    /// </summary>
    /// <param name="stream">A YAML string from a stream.</param>
    /// <returns>An instance of the YAML data.</returns>
    public object Deserialize(Stream stream)
    {
        var serializer = GetYamlSerializer();
        return serializer.Deserialize(stream);
    }

    /// <summary>
    /// Reset the assembly cache used by this class.
    /// </summary>
    public override void ResetCache()
    {
        lock (Lock)
        {
            // Reset the current serializer as the set of assemblies has changed
            globalSerializer = null;
        }
    }

    protected virtual ISerializerFactorySelector CreateSelector()
    {
        return new ProfileSerializerFactorySelector(YamlSerializerFactoryAttribute.Default);
    }

    protected Serializer GetYamlSerializer()
    {
        // Cache serializer to improve performance
        var localSerializer = CreateSerializer(ref globalSerializer);
        return localSerializer;
    }

    private Serializer CreateSerializer(ref Serializer? localSerializer)
    {
        // Early exit if already initialized
        if (localSerializer != null)
            return localSerializer;

        lock (Lock)
        {
            if (localSerializer == null)
            {
                // var clock = Stopwatch.StartNew();

                var config = new SerializerSettings
                {
                    EmitAlias = false,
                    LimitPrimitiveFlowSequence = 0,
                    Attributes = new AttributeRegistry(),
                    PreferredIndent = 4,
                    EmitShortTypeName = true,
                    ComparerForKeySorting = new DefaultMemberComparer(),
                    SerializerFactorySelector = CreateSelector(),
                };

                for (int index = RegisteredAssemblies.Count - 1; index >= 0; index--)
                {
                    var registeredAssembly = RegisteredAssemblies[index];
                    config.RegisterAssembly(registeredAssembly);
                }

                var newSerializer = new Serializer(config);
                newSerializer.Settings.ObjectSerializerBackend = new DefaultObjectSerializerBackend();

                // Log.Info("New YAML serializer created in {0}ms", clock.ElapsedMilliseconds);
                localSerializer = newSerializer;
            }
        }

        return localSerializer;
    }
}
