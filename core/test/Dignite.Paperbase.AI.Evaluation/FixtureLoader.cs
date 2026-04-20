using System.Collections.Generic;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Dignite.Paperbase.AI.Evaluation;

public static class FixtureLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static IReadOnlyList<ClassificationFixture> LoadClassificationFixtures(string fixturesDir)
    {
        var results = new List<ClassificationFixture>();
        var dir = Path.Combine(fixturesDir, "classification");
        if (!Directory.Exists(dir))
            return results;

        foreach (var file in Directory.GetFiles(dir, "*.yaml"))
        {
            var yaml = File.ReadAllText(file);
            var fixture = Deserializer.Deserialize<ClassificationFixture>(yaml);
            if (string.IsNullOrEmpty(fixture.Id))
                fixture.Id = Path.GetFileNameWithoutExtension(file);
            results.Add(fixture);
        }
        return results;
    }

    public static EvaluationThresholds LoadThresholds(string fixturesDir)
    {
        var path = Path.Combine(fixturesDir, "thresholds.yaml");
        if (!File.Exists(path))
            return new EvaluationThresholds();

        var yaml = File.ReadAllText(path);
        return Deserializer.Deserialize<EvaluationThresholds>(yaml);
    }
}
