using System.Reflection;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace PTDoc.Tests.Integration;

[Trait("Category", "CoreCi")]
public sealed class EncounterIntakeMigrationTests
{
    [Theory]
    [InlineData("SqlServer", "Microsoft.EntityFrameworkCore.SqlServer")]
    [InlineData("Postgres", "Npgsql.EntityFrameworkCore.PostgreSQL")]
    public void Downgrade_RemovesOnlyTheEncounterAssociation_WithoutRecreatingExistingIndexes(string assemblySuffix, string provider)
    {
        var assembly = Assembly.Load("PTDoc.Infrastructure.Migrations." + assemblySuffix);
        var type = assembly.GetType("PTDoc.Infrastructure.Data.Migrations.AddEncounterIntakeAssociation", throwOnError: true)!;
        var migration = Assert.IsAssignableFrom<Migration>(Activator.CreateInstance(type));
        migration.ActiveProvider = provider;

        var addedColumns = migration.UpOperations.OfType<AddColumnOperation>().Select(value => value.Name).Order().ToArray();
        var removedColumns = migration.DownOperations.OfType<DropColumnOperation>().Select(value => value.Name).Order().ToArray();
        Assert.Equal(new[] { "EncounterIsReEvaluation", "EncounterNoteId", "QuestionnaireDefinitionId" }, addedColumns);
        Assert.Equal(addedColumns, removedColumns);
        Assert.DoesNotContain(migration.DownOperations, operation => operation is CreateIndexOperation);
        Assert.All(migration.DownOperations, operation => Assert.Equal("IntakeForms", operation switch
        {
            DropColumnOperation column => column.Table,
            DropIndexOperation index => index.Table,
            DropForeignKeyOperation foreignKey => foreignKey.Table,
            _ => throw new InvalidOperationException("Encounter-intake downgrade touched an unrelated schema object.")
        }));
    }
}
