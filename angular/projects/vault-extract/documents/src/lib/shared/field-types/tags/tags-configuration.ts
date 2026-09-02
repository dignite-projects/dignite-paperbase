/**
 * Configuration of a `Tags` field, shaped for `FormBuilder.group()`. Mirrors the server's
 * `TagsConfiguration` (`Dignite.Vault.Extract.FlexFields`).
 *
 * The property names are the **stored** configuration keys, not a naming choice — same rule the
 * kernel's own configuration models follow.
 */
export class TagsConfiguration {
  'Tags.MaxCount': unknown = [100];

  'Tags.MaxLength': unknown = [256];

  'Tags.Placeholder': unknown = [''];
}
