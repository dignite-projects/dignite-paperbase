const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

// Deliberately NOT extending eslint.configs.recommended / tseslint.configs.recommended here:
// the pre-flat-config setup only ever extended @angular-eslint's own recommended sets, so pulling
// those in now would newly enable rules (e.g. no-explicit-any) across files - like the generated
// HTTP proxy layer - that were never linted against them. This migration preserves the existing
// enforced ruleset; adopting the broader recommended sets is a separate, deliberate decision.
module.exports = tseslint.config(
  {
    files: ['**/*.ts'],
    extends: [...angular.configs.tsRecommended],
    processor: angular.processInlineTemplates,
  },
  {
    files: ['**/*.html'],
    extends: [...angular.configs.templateRecommended],
    rules: {},
  },
);
