# Contracts - Angular Library

This is an Angular library for the ABP module. It provides UI components and services that can be used in Angular applications. For more information, visit [abp.io](https://abp.io/).

## Pre-requirements

* [Node.js v18 or later](https://nodejs.org/)
* [npm](https://www.npmjs.com/) or [yarn](https://yarnpkg.com/)

## Getting Started

### Install dependencies

```bash
npm install
```

## Development

### Build the library

```bash
ng build contracts
```

The build artifacts will be stored in the `dist/` directory.

### Watch mode

For development with automatic rebuilds:

```bash
ng build contracts --watch
```

## Code scaffolding

Run `ng generate component component-name --project contracts` to generate a new component in the library.

## Running unit tests

Run `ng test contracts` to execute the unit tests via [Karma](https://karma-runner.github.io).

## Publishing

After building the library, you can publish it to npm:

```bash
cd dist/contracts
npm publish
```

## Using in a Host Application

To use this library in an ABP Angular application:

1. Install the package:
```bash
npm install @dignite.Paperbase/contracts
```

2. Import the module in your application:
```typescript
import { ContractsModule } from '@dignite.Paperbase/contracts';

@NgModule({
  imports: [
    // ...
    ContractsModule
  ]
})
export class AppModule { }
```

## Additional Resources

* [ABP Angular UI Documentation](https://abp.io/docs/latest/framework/ui/angular/overview)
* [Angular Library Development](https://angular.dev/tools/libraries)
* [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli)
