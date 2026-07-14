using Microsoft.EntityFrameworkCore.Migrations;

namespace PTDoc.Infrastructure.Data.Migrations;

/// <summary>
/// Provider-neutral migration operations shared by the SQLite, SQL Server, and
/// PostgreSQL migration assemblies. Column types are resolved by each provider.
/// </summary>
public static class IntegrationMigrationOperations
{
    public static void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "StorageKey",
            table: "PatientDocuments",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "IntegrationConnections",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ClinicId = table.Column<Guid>(nullable: false),
                Provider = table.Column<string>(maxLength: 50, nullable: false),
                DisplayName = table.Column<string>(maxLength: 160, nullable: false),
                IsEnabled = table.Column<bool>(nullable: false),
                ConfigurationJson = table.Column<string>(nullable: false),
                SecretReference = table.Column<string>(maxLength: 500, nullable: false),
                WebhookTokenHash = table.Column<string>(maxLength: 64, nullable: true),
                ComplianceApprovedAtUtc = table.Column<DateTime>(nullable: true),
                ComplianceApprovedByUserId = table.Column<Guid>(nullable: true),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(nullable: false),
                LastVerifiedAtUtc = table.Column<DateTime>(nullable: true),
                LastHealthCode = table.Column<string>(maxLength: 100, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationConnections", x => x.Id);
                table.ForeignKey("FK_IntegrationConnections_Clinics_ClinicId", x => x.ClinicId, "Clinics", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "IntegrationExternalMappings",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ClinicId = table.Column<Guid>(nullable: false),
                IntegrationConnectionId = table.Column<Guid>(nullable: false),
                EntityType = table.Column<string>(maxLength: 80, nullable: false),
                InternalEntityId = table.Column<Guid>(nullable: false),
                ExternalId = table.Column<string>(maxLength: 255, nullable: false),
                IsActive = table.Column<bool>(nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                LastSyncedAtUtc = table.Column<DateTime>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationExternalMappings", x => x.Id);
                table.ForeignKey("FK_IntegrationExternalMappings_Clinics_ClinicId", x => x.ClinicId, "Clinics", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_IntegrationExternalMappings_IntegrationConnections_IntegrationConnectionId", x => x.IntegrationConnectionId, "IntegrationConnections", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "IntegrationOutboxItems",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ClinicId = table.Column<Guid>(nullable: false),
                IntegrationConnectionId = table.Column<Guid>(nullable: false),
                JobType = table.Column<string>(maxLength: 100, nullable: false),
                AggregateType = table.Column<string>(maxLength: 80, nullable: false),
                AggregateId = table.Column<Guid>(nullable: false),
                PayloadJson = table.Column<string>(nullable: false),
                IdempotencyKey = table.Column<string>(maxLength: 255, nullable: false),
                CorrelationId = table.Column<string>(maxLength: 100, nullable: false),
                Status = table.Column<int>(nullable: false),
                AttemptCount = table.Column<int>(nullable: false),
                MaxAttempts = table.Column<int>(nullable: false),
                NextAttemptAtUtc = table.Column<DateTime>(nullable: false),
                LeaseExpiresAtUtc = table.Column<DateTime>(nullable: true),
                LeaseOwner = table.Column<string>(maxLength: 160, nullable: true),
                LastErrorCode = table.Column<string>(maxLength: 160, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(nullable: false),
                CompletedAtUtc = table.Column<DateTime>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationOutboxItems", x => x.Id);
                table.ForeignKey("FK_IntegrationOutboxItems_IntegrationConnections_IntegrationConnectionId", x => x.IntegrationConnectionId, "IntegrationConnections", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "IntegrationSyncCheckpoints",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ClinicId = table.Column<Guid>(nullable: false),
                IntegrationConnectionId = table.Column<Guid>(nullable: false),
                SyncType = table.Column<string>(maxLength: 100, nullable: false),
                LastSuccessfulAtUtc = table.Column<DateTime>(nullable: true),
                Cursor = table.Column<string>(maxLength: 500, nullable: true),
                UpdatedAtUtc = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationSyncCheckpoints", x => x.Id);
                table.ForeignKey("FK_IntegrationSyncCheckpoints_IntegrationConnections_IntegrationConnectionId", x => x.IntegrationConnectionId, "IntegrationConnections", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "IntegrationConflicts",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ClinicId = table.Column<Guid>(nullable: false),
                IntegrationConnectionId = table.Column<Guid>(nullable: false),
                EntityType = table.Column<string>(maxLength: 80, nullable: false),
                InternalEntityId = table.Column<Guid>(nullable: false),
                ConflictType = table.Column<string>(maxLength: 100, nullable: false),
                DetailsJson = table.Column<string>(nullable: false),
                Status = table.Column<int>(nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                ResolvedAtUtc = table.Column<DateTime>(nullable: true),
                ResolvedByUserId = table.Column<Guid>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IntegrationConflicts", x => x.Id);
                table.ForeignKey("FK_IntegrationConflicts_IntegrationConnections_IntegrationConnectionId", x => x.IntegrationConnectionId, "IntegrationConnections", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ProcessedIntegrationWebhooks",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ClinicId = table.Column<Guid>(nullable: false),
                IntegrationConnectionId = table.Column<Guid>(nullable: false),
                ProviderMessageId = table.Column<string>(maxLength: 255, nullable: false),
                EventType = table.Column<string>(maxLength: 100, nullable: false),
                PayloadHashSha256 = table.Column<string>(maxLength: 64, nullable: false),
                ReceivedAtUtc = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProcessedIntegrationWebhooks", x => x.Id);
                table.ForeignKey("FK_ProcessedIntegrationWebhooks_IntegrationConnections_IntegrationConnectionId", x => x.IntegrationConnectionId, "IntegrationConnections", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "FaxTransmissions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ClinicId = table.Column<Guid>(nullable: false),
                IntegrationConnectionId = table.Column<Guid>(nullable: false),
                PatientId = table.Column<Guid>(nullable: true),
                SourceDocumentId = table.Column<Guid>(nullable: true),
                SourceClinicalNoteId = table.Column<Guid>(nullable: true),
                OriginalTransmissionId = table.Column<Guid>(nullable: true),
                RequestedByUserId = table.Column<Guid>(nullable: false),
                ClientCorrelationId = table.Column<string>(maxLength: 100, nullable: false),
                ProviderFaxId = table.Column<string>(maxLength: 100, nullable: true),
                DocumentStorageKey = table.Column<string>(maxLength: 1024, nullable: false),
                DocumentFileName = table.Column<string>(maxLength: 255, nullable: false),
                DocumentContentType = table.Column<string>(maxLength: 120, nullable: false),
                DocumentHashSha256 = table.Column<string>(maxLength: 64, nullable: false),
                DocumentSizeBytes = table.Column<long>(nullable: false),
                DocumentType = table.Column<string>(maxLength: 80, nullable: false),
                CoverSubject = table.Column<string>(maxLength: 1045, nullable: true),
                CoverMessage = table.Column<string>(maxLength: 9945, nullable: true),
                IncludeCoverSheet = table.Column<bool>(nullable: false),
                Status = table.Column<int>(nullable: false),
                ProviderStatus = table.Column<string>(maxLength: 100, nullable: true),
                FailureCode = table.Column<string>(maxLength: 160, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(nullable: false),
                SubmittedAtUtc = table.Column<DateTime>(nullable: true),
                CompletedAtUtc = table.Column<DateTime>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FaxTransmissions", x => x.Id);
                table.ForeignKey("FK_FaxTransmissions_IntegrationConnections_IntegrationConnectionId", x => x.IntegrationConnectionId, "IntegrationConnections", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_FaxTransmissions_Patients_PatientId", x => x.PatientId, "Patients", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "InboundFaxes",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ClinicId = table.Column<Guid>(nullable: false),
                IntegrationConnectionId = table.Column<Guid>(nullable: false),
                ProviderFaxId = table.Column<string>(maxLength: 100, nullable: false),
                ProviderStatus = table.Column<string>(maxLength: 100, nullable: false),
                FromNumber = table.Column<string>(maxLength: 20, nullable: false),
                ToNumber = table.Column<string>(maxLength: 20, nullable: false),
                SenderName = table.Column<string>(maxLength: 245, nullable: true),
                PageCount = table.Column<int>(nullable: false),
                DocumentStorageKey = table.Column<string>(maxLength: 1024, nullable: false),
                DocumentFileName = table.Column<string>(maxLength: 255, nullable: false),
                DocumentContentType = table.Column<string>(maxLength: 120, nullable: false),
                DocumentHashSha256 = table.Column<string>(maxLength: 64, nullable: false),
                DocumentSizeBytes = table.Column<long>(nullable: false),
                Status = table.Column<int>(nullable: false),
                AssignedPatientId = table.Column<Guid>(nullable: true),
                PatientDocumentId = table.Column<Guid>(nullable: true),
                AssignedByUserId = table.Column<Guid>(nullable: true),
                ReceivedAtUtc = table.Column<DateTime>(nullable: false),
                AssignedAtUtc = table.Column<DateTime>(nullable: true),
                AssignmentReason = table.Column<string>(maxLength: 1000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_InboundFaxes", x => x.Id);
                table.ForeignKey("FK_InboundFaxes_IntegrationConnections_IntegrationConnectionId", x => x.IntegrationConnectionId, "IntegrationConnections", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InboundFaxes_Patients_AssignedPatientId", x => x.AssignedPatientId, "Patients", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_InboundFaxes_PatientDocuments_PatientDocumentId", x => x.PatientDocumentId, "PatientDocuments", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "HepPrograms",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ClinicId = table.Column<Guid>(nullable: false),
                IntegrationConnectionId = table.Column<Guid>(nullable: false),
                PatientId = table.Column<Guid>(nullable: false),
                CreatedByUserId = table.Column<Guid>(nullable: false),
                CurrentRevisionId = table.Column<Guid>(nullable: true),
                ProviderProgramId = table.Column<string>(maxLength: 255, nullable: true),
                ProviderEpisodeId = table.Column<string>(maxLength: 255, nullable: true),
                Status = table.Column<int>(nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(nullable: false),
                LastSyncedAtUtc = table.Column<DateTime>(nullable: true),
                LastTrackingSyncAtUtc = table.Column<DateTime>(nullable: true),
                LastFailureCode = table.Column<string>(maxLength: 160, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HepPrograms", x => x.Id);
                table.ForeignKey("FK_HepPrograms_Clinics_ClinicId", x => x.ClinicId, "Clinics", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_HepPrograms_IntegrationConnections_IntegrationConnectionId", x => x.IntegrationConnectionId, "IntegrationConnections", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_HepPrograms_Patients_PatientId", x => x.PatientId, "Patients", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_HepPrograms_Users_CreatedByUserId", x => x.CreatedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "FaxRecipients",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                FaxTransmissionId = table.Column<Guid>(nullable: false),
                FaxNumber = table.Column<string>(maxLength: 20, nullable: false),
                RecipientName = table.Column<string>(maxLength: 245, nullable: true),
                Status = table.Column<int>(nullable: false),
                ProviderStatus = table.Column<string>(maxLength: 100, nullable: true),
                FailureCode = table.Column<string>(maxLength: 160, nullable: true),
                AttemptCount = table.Column<int>(nullable: false),
                CompletedAtUtc = table.Column<DateTime>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FaxRecipients", x => x.Id);
                table.ForeignKey("FK_FaxRecipients_FaxTransmissions_FaxTransmissionId", x => x.FaxTransmissionId, "FaxTransmissions", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "FaxStatusEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                FaxTransmissionId = table.Column<Guid>(nullable: false),
                Status = table.Column<int>(nullable: false),
                ProviderStatus = table.Column<string>(maxLength: 100, nullable: true),
                FailureCode = table.Column<string>(maxLength: 160, nullable: true),
                Source = table.Column<string>(maxLength: 40, nullable: false),
                OccurredAtUtc = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FaxStatusEvents", x => x.Id);
                table.ForeignKey("FK_FaxStatusEvents_FaxTransmissions_FaxTransmissionId", x => x.FaxTransmissionId, "FaxTransmissions", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "HepProgramRevisions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                HepProgramId = table.Column<Guid>(nullable: false),
                Version = table.Column<int>(nullable: false),
                Source = table.Column<int>(nullable: false),
                Title = table.Column<string>(maxLength: 255, nullable: false),
                TherapistNotes = table.Column<string>(maxLength: 4000, nullable: true),
                StartDate = table.Column<DateOnly>(nullable: true),
                EndDate = table.Column<DateOnly>(nullable: true),
                CreatedByUserId = table.Column<Guid>(nullable: false),
                CreatedAtUtc = table.Column<DateTime>(nullable: false),
                PublishedAtUtc = table.Column<DateTime>(nullable: true),
                ProviderVersion = table.Column<string>(maxLength: 255, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HepProgramRevisions", x => x.Id);
                table.ForeignKey("FK_HepProgramRevisions_HepPrograms_HepProgramId", x => x.HepProgramId, "HepPrograms", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_HepProgramRevisions_Users_CreatedByUserId", x => x.CreatedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "HepTrackingObservations",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ClinicId = table.Column<Guid>(nullable: false),
                HepProgramId = table.Column<Guid>(nullable: false),
                ProviderObservationId = table.Column<string>(maxLength: 255, nullable: false),
                ExternalExerciseId = table.Column<string>(maxLength: 255, nullable: true),
                Code = table.Column<string>(maxLength: 80, nullable: false),
                Value = table.Column<string>(maxLength: 255, nullable: false),
                UnitOfMeasure = table.Column<string>(maxLength: 80, nullable: true),
                ActivityAtUtc = table.Column<DateTime>(nullable: false),
                ImportedAtUtc = table.Column<DateTime>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HepTrackingObservations", x => x.Id);
                table.ForeignKey("FK_HepTrackingObservations_HepPrograms_HepProgramId", x => x.HepProgramId, "HepPrograms", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "HepPrescriptionExercises",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                HepProgramRevisionId = table.Column<Guid>(nullable: false),
                SortOrder = table.Column<int>(nullable: false),
                ExternalExerciseId = table.Column<string>(maxLength: 255, nullable: false),
                Title = table.Column<string>(maxLength: 500, nullable: false),
                DescriptionOverride = table.Column<string>(maxLength: 4000, nullable: true),
                Sets = table.Column<string>(maxLength: 100, nullable: true),
                Repetitions = table.Column<string>(maxLength: 100, nullable: true),
                Weight = table.Column<string>(maxLength: 100, nullable: true),
                Frequency = table.Column<string>(maxLength: 200, nullable: true),
                Duration = table.Column<string>(maxLength: 100, nullable: true),
                Hold = table.Column<string>(maxLength: 100, nullable: true),
                Tempo = table.Column<string>(maxLength: 100, nullable: true),
                Rest = table.Column<string>(maxLength: 100, nullable: true),
                Level = table.Column<string>(maxLength: 100, nullable: true),
                Other = table.Column<string>(maxLength: 1000, nullable: true),
                IsHomeExercise = table.Column<bool>(nullable: false),
                Mirror = table.Column<bool>(nullable: false),
                Flip = table.Column<bool>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_HepPrescriptionExercises", x => x.Id);
                table.ForeignKey("FK_HepPrescriptionExercises_HepProgramRevisions_HepProgramRevisionId", x => x.HepProgramRevisionId, "HepProgramRevisions", "Id", onDelete: ReferentialAction.Cascade);
            });

        CreateIndexes(migrationBuilder);
    }

    private static void CreateIndexes(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex("IX_IntegrationConnections_ClinicId_Provider", "IntegrationConnections", new[] { "ClinicId", "Provider" }, unique: true);
        migrationBuilder.CreateIndex("IX_IntegrationConnections_IsEnabled", "IntegrationConnections", "IsEnabled");
        migrationBuilder.CreateIndex("IX_IntegrationExternalMappings_ClinicId", "IntegrationExternalMappings", "ClinicId");
        migrationBuilder.CreateIndex("IX_IntegrationExternalMappings_IntegrationConnectionId_EntityType_InternalEntityId", "IntegrationExternalMappings", new[] { "IntegrationConnectionId", "EntityType", "InternalEntityId" }, unique: true);
        migrationBuilder.CreateIndex("IX_IntegrationExternalMappings_IntegrationConnectionId_EntityType_ExternalId", "IntegrationExternalMappings", new[] { "IntegrationConnectionId", "EntityType", "ExternalId" }, unique: true);
        migrationBuilder.CreateIndex("IX_IntegrationOutboxItems_Status_NextAttemptAtUtc", "IntegrationOutboxItems", new[] { "Status", "NextAttemptAtUtc" });
        migrationBuilder.CreateIndex("IX_IntegrationOutboxItems_IntegrationConnectionId_IdempotencyKey", "IntegrationOutboxItems", new[] { "IntegrationConnectionId", "IdempotencyKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_IntegrationOutboxItems_AggregateType_AggregateId", "IntegrationOutboxItems", new[] { "AggregateType", "AggregateId" });
        migrationBuilder.CreateIndex("IX_IntegrationSyncCheckpoints_IntegrationConnectionId_SyncType", "IntegrationSyncCheckpoints", new[] { "IntegrationConnectionId", "SyncType" }, unique: true);
        migrationBuilder.CreateIndex("IX_IntegrationConflicts_IntegrationConnectionId_Status", "IntegrationConflicts", new[] { "IntegrationConnectionId", "Status" });
        migrationBuilder.CreateIndex("IX_IntegrationConflicts_EntityType_InternalEntityId", "IntegrationConflicts", new[] { "EntityType", "InternalEntityId" });
        migrationBuilder.CreateIndex("IX_ProcessedIntegrationWebhooks_IntegrationConnectionId_ProviderMessageId", "ProcessedIntegrationWebhooks", new[] { "IntegrationConnectionId", "ProviderMessageId" }, unique: true);
        migrationBuilder.CreateIndex("IX_FaxTransmissions_ClinicId_CreatedAtUtc", "FaxTransmissions", new[] { "ClinicId", "CreatedAtUtc" });
        migrationBuilder.CreateIndex("IX_FaxTransmissions_PatientId", "FaxTransmissions", "PatientId");
        migrationBuilder.CreateIndex("IX_FaxTransmissions_ProviderFaxId", "FaxTransmissions", "ProviderFaxId");
        migrationBuilder.CreateIndex("IX_FaxTransmissions_IntegrationConnectionId_ClientCorrelationId", "FaxTransmissions", new[] { "IntegrationConnectionId", "ClientCorrelationId" }, unique: true);
        migrationBuilder.CreateIndex("IX_FaxRecipients_FaxTransmissionId", "FaxRecipients", "FaxTransmissionId");
        migrationBuilder.CreateIndex("IX_FaxStatusEvents_FaxTransmissionId_OccurredAtUtc", "FaxStatusEvents", new[] { "FaxTransmissionId", "OccurredAtUtc" });
        migrationBuilder.CreateIndex("IX_InboundFaxes_IntegrationConnectionId_ProviderFaxId", "InboundFaxes", new[] { "IntegrationConnectionId", "ProviderFaxId" }, unique: true);
        migrationBuilder.CreateIndex("IX_InboundFaxes_ClinicId_Status_ReceivedAtUtc", "InboundFaxes", new[] { "ClinicId", "Status", "ReceivedAtUtc" });
        migrationBuilder.CreateIndex("IX_InboundFaxes_AssignedPatientId", "InboundFaxes", "AssignedPatientId");
        migrationBuilder.CreateIndex("IX_InboundFaxes_PatientDocumentId", "InboundFaxes", "PatientDocumentId");
        migrationBuilder.CreateIndex("IX_HepPrograms_ClinicId", "HepPrograms", "ClinicId");
        migrationBuilder.CreateIndex("IX_HepPrograms_IntegrationConnectionId", "HepPrograms", "IntegrationConnectionId");
        migrationBuilder.CreateIndex("IX_HepPrograms_CreatedByUserId", "HepPrograms", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_HepPrograms_PatientId_UpdatedAtUtc", "HepPrograms", new[] { "PatientId", "UpdatedAtUtc" });
        migrationBuilder.CreateIndex("IX_HepPrograms_CurrentRevisionId", "HepPrograms", "CurrentRevisionId");
        migrationBuilder.CreateIndex("IX_HepProgramRevisions_CreatedByUserId", "HepProgramRevisions", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_HepProgramRevisions_HepProgramId_Version", "HepProgramRevisions", new[] { "HepProgramId", "Version" }, unique: true);
        migrationBuilder.CreateIndex("IX_HepPrescriptionExercises_HepProgramRevisionId_SortOrder", "HepPrescriptionExercises", new[] { "HepProgramRevisionId", "SortOrder" }, unique: true);
        migrationBuilder.CreateIndex("IX_HepTrackingObservations_HepProgramId_ProviderObservationId", "HepTrackingObservations", new[] { "HepProgramId", "ProviderObservationId" }, unique: true);
        migrationBuilder.CreateIndex("IX_HepTrackingObservations_HepProgramId_ActivityAtUtc", "HepTrackingObservations", new[] { "HepProgramId", "ActivityAtUtc" });
    }

    public static void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("FaxRecipients");
        migrationBuilder.DropTable("FaxStatusEvents");
        migrationBuilder.DropTable("InboundFaxes");
        migrationBuilder.DropTable("HepPrescriptionExercises");
        migrationBuilder.DropTable("HepTrackingObservations");
        migrationBuilder.DropTable("IntegrationConflicts");
        migrationBuilder.DropTable("IntegrationExternalMappings");
        migrationBuilder.DropTable("IntegrationOutboxItems");
        migrationBuilder.DropTable("IntegrationSyncCheckpoints");
        migrationBuilder.DropTable("ProcessedIntegrationWebhooks");
        migrationBuilder.DropTable("FaxTransmissions");
        migrationBuilder.DropTable("HepProgramRevisions");
        migrationBuilder.DropTable("HepPrograms");
        migrationBuilder.DropTable("IntegrationConnections");
        migrationBuilder.DropColumn("StorageKey", "PatientDocuments");
    }
}
