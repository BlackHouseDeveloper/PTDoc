using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20261103030000_AddOrganizationMemberships")]
public partial class AddOrganizationMemberships : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE [Organizations] (
                [Id] uniqueidentifier NOT NULL,
                [Name] nvarchar(200) NOT NULL,
                [IsActive] bit NOT NULL,
                [Version] bigint NOT NULL,
                [CreatedAtUtc] datetime2 NOT NULL,
                CONSTRAINT [PK_Organizations] PRIMARY KEY ([Id])
            );
            ALTER TABLE [Clinics] ADD [OrganizationId] uniqueidentifier NULL;
            ALTER TABLE [Clinics] ADD CONSTRAINT [FK_Clinics_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]);
            CREATE INDEX [IX_Clinics_OrganizationId] ON [Clinics] ([OrganizationId]);
            ALTER TABLE [Sessions] ADD [ClinicId] uniqueidentifier NULL;
            """);
        // SQL Server must compile the backfill after the new columns exist.
        migrationBuilder.Sql("""
            INSERT INTO [Organizations] ([Id], [Name], [IsActive], [Version], [CreatedAtUtc]) SELECT [Id], [Name], [IsActive], 1, [CreatedAt] FROM [Clinics];
            UPDATE [Clinics] SET [OrganizationId] = [Id];
            CREATE TABLE [OrganizationMemberships] (
                [OrganizationId] uniqueidentifier NOT NULL,
                [UserId] uniqueidentifier NOT NULL,
                [Role] int NOT NULL,
                [IsActive] bit NOT NULL,
                [Version] bigint NOT NULL,
                CONSTRAINT [PK_OrganizationMemberships] PRIMARY KEY ([OrganizationId], [UserId]),
                CONSTRAINT [FK_OrganizationMemberships_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]),
                CONSTRAINT [FK_OrganizationMemberships_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
            );
            CREATE INDEX [IX_OrganizationMemberships_UserId] ON [OrganizationMemberships] ([UserId]);
            CREATE TABLE [ClinicMemberships] (
                [ClinicId] uniqueidentifier NOT NULL,
                [UserId] uniqueidentifier NOT NULL,
                [Role] nvarchar(50) NOT NULL,
                [IsActive] bit NOT NULL,
                [Version] bigint NOT NULL,
                CONSTRAINT [PK_ClinicMemberships] PRIMARY KEY ([ClinicId], [UserId]),
                CONSTRAINT [FK_ClinicMemberships_Clinics_ClinicId] FOREIGN KEY ([ClinicId]) REFERENCES [Clinics] ([Id]),
                CONSTRAINT [FK_ClinicMemberships_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
            );
            CREATE INDEX [IX_ClinicMemberships_UserId] ON [ClinicMemberships] ([UserId]);
            CREATE TABLE [OrganizationInvitations] (
                [Id] uniqueidentifier NOT NULL,
                [OrganizationId] uniqueidentifier NOT NULL,
                [Email] nvarchar(255) NOT NULL,
                [TokenHash] nvarchar(64) NOT NULL,
                [ExpiresAtUtc] datetime2 NOT NULL,
                [AcceptedAtUtc] datetime2 NULL,
                [InvitedByUserId] uniqueidentifier NOT NULL,
                [Version] bigint NOT NULL,
                CONSTRAINT [PK_OrganizationInvitations] PRIMARY KEY ([Id]),
                CONSTRAINT [FK_OrganizationInvitations_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [Organizations] ([Id]),
                CONSTRAINT [FK_OrganizationInvitations_Users_InvitedByUserId] FOREIGN KEY ([InvitedByUserId]) REFERENCES [Users] ([Id])
            );
            CREATE INDEX [IX_OrganizationInvitations_OrganizationId] ON [OrganizationInvitations] ([OrganizationId]);
            CREATE INDEX [IX_OrganizationInvitations_InvitedByUserId] ON [OrganizationInvitations] ([InvitedByUserId]);
            CREATE UNIQUE INDEX [IX_OrganizationInvitations_TokenHash] ON [OrganizationInvitations] ([TokenHash]);
            INSERT INTO [OrganizationMemberships] ([OrganizationId], [UserId], [Role], [IsActive], [Version])
            SELECT c.[OrganizationId], u.[Id], CASE WHEN u.[IsActive] = 1 AND u.[Role] IN ('Admin','Owner') THEN 2 ELSE 0 END, 1, 1
            FROM [Users] u JOIN [Clinics] c ON c.[Id] = u.[ClinicId];
            INSERT INTO [ClinicMemberships] ([ClinicId], [UserId], [Role], [IsActive], [Version])
            SELECT [ClinicId], [Id], [Role], 1, 1 FROM [Users] WHERE [ClinicId] IS NOT NULL;
            ALTER TABLE [ScheduleBlockRules] DROP CONSTRAINT [FK_ScheduleBlockRules_Users_ClinicId_ClinicianId];
            ALTER TABLE [ScheduleBlockRules] ADD CONSTRAINT [FK_ScheduleBlockRules_ClinicMemberships_ClinicId_ClinicianId] FOREIGN KEY ([ClinicId], [ClinicianId]) REFERENCES [ClinicMemberships] ([ClinicId], [UserId]);
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF EXISTS (SELECT 1 FROM [ClinicMemberships] m JOIN [Users] u ON u.[Id] = m.[UserId]
            WHERE u.[ClinicId] IS NULL OR m.[ClinicId] <> u.[ClinicId] OR m.[Role] <> u.[Role] OR m.[IsActive] <> 1)
            OR EXISTS (SELECT 1 FROM [OrganizationMemberships] WHERE [IsActive] <> 1)
            OR EXISTS (SELECT 1 FROM [Organizations] WHERE [IsActive] <> 1)
            OR EXISTS (SELECT 1 FROM [Clinics] WHERE [OrganizationId] <> [Id])
            OR EXISTS (SELECT 1 FROM [OrganizationMemberships] m JOIN [Users] u ON u.[Id] = m.[UserId]
                WHERE u.[ClinicId] IS NULL OR m.[OrganizationId] <> u.[ClinicId]
                    OR m.[Role] <> CASE WHEN u.[IsActive] = 1 AND u.[Role] IN ('Admin','Owner') THEN 2 ELSE 0 END)
            OR EXISTS (SELECT 1 FROM [Sessions] s JOIN [Users] u ON u.[Id] = s.[UserId]
                WHERE s.[ClinicId] IS NOT NULL AND (u.[ClinicId] IS NULL OR s.[ClinicId] <> u.[ClinicId]))
            OR EXISTS (SELECT 1 FROM [OrganizationInvitations]) THROW 51005, 'Reconcile organization memberships before rollback.', 1;
            ALTER TABLE [ScheduleBlockRules] DROP CONSTRAINT [FK_ScheduleBlockRules_ClinicMemberships_ClinicId_ClinicianId];
            ALTER TABLE [ScheduleBlockRules] ADD CONSTRAINT [FK_ScheduleBlockRules_Users_ClinicId_ClinicianId] FOREIGN KEY ([ClinicId], [ClinicianId]) REFERENCES [Users] ([ClinicId], [Id]);
            DROP TABLE [OrganizationInvitations];
            DROP TABLE [ClinicMemberships];
            DROP TABLE [OrganizationMemberships];
            ALTER TABLE [Clinics] DROP CONSTRAINT [FK_Clinics_Organizations_OrganizationId];
            DROP INDEX [IX_Clinics_OrganizationId] ON [Clinics];
            ALTER TABLE [Clinics] DROP COLUMN [OrganizationId];
            ALTER TABLE [Sessions] DROP COLUMN [ClinicId];
            DROP TABLE [Organizations];
            """);
    }
}
