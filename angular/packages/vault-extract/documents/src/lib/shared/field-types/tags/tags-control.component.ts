import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CoreModule } from '@abp/ng.core';
import { AbstractControl, FormControl, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { FieldTypeControlBase } from '@dignite/ng.flex-fields';
import { TagsConfiguration } from './tags-configuration';

/**
 * Edits the value of a `Tags` field — a list of short free-form strings, entered one at a time.
 *
 * The control's own value is the `string[]` the server stores; the text box below it is draft-only
 * component state, never itself the field value. `MaxCount` / `MaxLength` are enforced here too, as a
 * client convenience — the server's `FlexFieldValueReader` remains the actual gate, and rejects the
 * whole array rather than truncating it, so a value that slipped past this UI still fails loudly there.
 */
@Component({
  selector: 'ff-tags-control',
  templateUrl: './tags-control.component.html',
  imports: [CommonModule, CoreModule, FormsModule, ReactiveFormsModule],
})
export class TagsControlComponent extends FieldTypeControlBase {
  draft = '';

  protected get valueControl(): FormControl<string[]> {
    return this.fieldControl as FormControl<string[]>;
  }

  protected get tags(): string[] {
    return this.valueControl?.value ?? [];
  }

  protected get maxCount(): number {
    return Number(this.fieldValue?.field.configuration['Tags.MaxCount'] ?? 100);
  }

  protected get maxLength(): number {
    return Number(this.fieldValue?.field.configuration['Tags.MaxLength'] ?? 256);
  }

  protected configurationDefaults(): object {
    return new TagsConfiguration();
  }

  protected createControl(): AbstractControl {
    const validators = this.fieldValue!.required ? [Validators.required] : [];
    return this.fb.control<string[]>(
      Array.isArray(this.selectedValue) ? (this.selectedValue as string[]) : [],
      validators,
    );
  }

  /** Adds the draft text as a new tag on Enter or comma, and clears the draft either way. */
  commitDraft(event: Event): void {
    event.preventDefault();
    const text = this.draft.trim();
    this.draft = '';

    if (!text || this.tags.includes(text) || this.tags.length >= this.maxCount || text.length > this.maxLength) {
      return;
    }

    this.valueControl.setValue([...this.tags, text]);
  }

  removeTag(index: number): void {
    this.valueControl.setValue(this.tags.filter((_, i) => i !== index));
  }
}
