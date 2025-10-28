# Contributing to Games Database API

Thank you for your interest in contributing to the Games Database API. This document provides guidelines and information for contributors.

## Getting Started

1. **Fork the repository** on GitHub
2. **Clone your fork** locally:
   ```bash
   git clone https://github.com/your-username/GamesDatabase.Api.git
   cd GamesDatabase.Api
   ```
3. **Add the upstream repository**:
   ```bash
   git remote add upstream https://github.com/David-H-Afonso/GamesDatabase.Api.git
   ```

## Development Setup

1. Install .NET 9.0 SDK or higher

2. Restore dependencies:

   ```bash
   dotnet restore
   ```

3. Apply database migrations:

   ```bash
   dotnet ef database update
   ```

4. Run the development server:

   ```bash
   dotnet run
   ```

5. Access Swagger UI at `http://localhost:8080/swagger`

## Making Changes

1. **Create a new branch** for your feature or bugfix:

   ```bash
   git checkout -b feature/your-feature-name
   ```

2. **Make your changes** following the code style guidelines below

3. **Test your changes** thoroughly:

   - Ensure the project builds without errors
   - Test all affected API endpoints
   - Verify database migrations work correctly
   - Check authentication and authorization

4. **Commit your changes** with clear, descriptive commit messages:
   ```bash
   git commit -m "Add feature: description of what you added"
   ```

## Code Style Guidelines

### C# Code

- Follow Microsoft's C# coding conventions
- Use meaningful names for classes, methods, and variables
- Keep methods small and focused on a single responsibility
- Use async/await for asynchronous operations
- Add XML documentation comments for public APIs

### Entity Framework

- Use migrations for all database schema changes
- Name migrations descriptively (e.g., `AddPriceComparisonFields`)
- Test migrations both up and down
- Use proper relationships and navigation properties

### API Design

- Follow RESTful conventions
- Use appropriate HTTP status codes
- Return consistent response formats
- Include proper error messages and validation

### General

- Write unit tests for business logic
- Keep controllers thin, move logic to services
- Use dependency injection properly
- Handle exceptions appropriately
- Log important events and errors

## Database Migrations

When making database schema changes:

1. Create a new migration:

   ```bash
   dotnet ef migrations add YourMigrationName
   ```

2. Review the generated migration code

3. Test the migration:

   ```bash
   dotnet ef database update
   ```

4. Test rollback:

   ```bash
   dotnet ef database update PreviousMigrationName
   ```

5. Include the migration files in your commit

## Testing

Before submitting a pull request:

1. Build the project:

   ```bash
   dotnet build
   ```

2. Run all tests:

   ```bash
   dotnet test
   ```

3. Test with the frontend application:
   - Ensure all API endpoints work correctly
   - Verify authentication flows
   - Check data persistence

## Submitting Changes

1. **Push your changes** to your fork:

   ```bash
   git push origin feature/your-feature-name
   ```

2. **Create a Pull Request** on GitHub:

   - Provide a clear title and description
   - Reference any related issues
   - Explain what the changes do and why they're necessary
   - Include any breaking changes or migration notes

3. **Wait for review**:
   - Address any feedback from maintainers
   - Make requested changes in new commits
   - Keep the PR updated with the main branch if needed

## Pull Request Guidelines

- **One feature per PR**: Keep pull requests focused on a single feature or bugfix
- **Update documentation**: Update README.md, XML comments, or Swagger annotations if needed
- **Include migrations**: Ensure database migrations are included and tested
- **Test thoroughly**: Verify all existing functionality still works
- **Follow conventions**: Match the existing code style and patterns
- **Be responsive**: Reply to comments and reviews in a timely manner

## API Versioning

When making breaking changes to the API:

- Document the breaking changes clearly
- Consider API versioning strategies
- Provide migration guides for frontend developers
- Discuss major changes with maintainers first

## Security Considerations

When contributing, be mindful of:

- Never commit sensitive data (API keys, passwords, etc.)
- Use proper authentication and authorization
- Validate all user inputs
- Protect against SQL injection (use parameterized queries)
- Follow OWASP security guidelines

## Reporting Bugs

When reporting bugs, please include:

- A clear, descriptive title
- Steps to reproduce the issue
- Expected behavior vs actual behavior
- Error messages or logs
- Your environment (.NET version, OS, database)
- API request/response examples if applicable

## Suggesting Features

Feature suggestions are welcome. Please:

- Check existing issues to avoid duplicates
- Clearly describe the feature and its benefits
- Explain how it fits with the project's goals
- Consider backward compatibility
- Discuss API design implications

## Code of Conduct

- Be respectful and professional
- Welcome newcomers and help them learn
- Focus on constructive feedback
- Keep discussions on-topic
- Respect different perspectives and experiences

## Documentation

When adding new features:

- Update XML documentation comments
- Add or update Swagger annotations
- Update README.md if needed
- Document configuration options
- Provide usage examples

## Questions?

If you have questions about contributing, feel free to:

- Open an issue for discussion
- Ask in pull request comments
- Check existing documentation and issues first

Thank you for contributing to Games Database API!
