using Volo.Abp.Features;
using Volo.Abp.Validation.StringValues;

namespace Dignite.Paperbase.Features;

public class PaperbaseAIFeatureDefinitionProvider : FeatureDefinitionProvider
{
    public override void Define(IFeatureDefinitionContext context)
    {
        var group = context.AddGroup(
            PaperbaseAIFeatures.GroupName,
            displayName: LocalizableFeatureString("Feature:PaperbaseAI"));

        group.AddFeature(
            PaperbaseAIFeatures.MonthlyBudgetUsd,
            defaultValue: decimal.MaxValue.ToString(),
            displayName: LocalizableFeatureString("Feature:MonthlyBudgetUsd"),
            valueType: new FreeTextStringValueType());
    }

    private static Volo.Abp.Localization.FixedLocalizableString LocalizableFeatureString(string name)
        => new(name);
}
