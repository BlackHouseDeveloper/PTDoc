using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PTDoc.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicSettingsAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LegacyPinGraceEndsAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePin",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinChangedAtUtc",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "Clinics",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "America/Los_Angeles");

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "Clinics",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "AuthorizedOverlap",
                table: "Appointments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "VisitTypeId",
                table: "Appointments",
                type: "uuid",
                nullable: true);

            // Appointment.ClinicId remains nullable for the compatibility release. This unique
            // index lets reminder dispatches use a clinic-qualified foreign key without making
            // legacy appointments non-nullable prematurely.
            migrationBuilder.CreateIndex(
                name: "UX_Appointments_ClinicId_Id_ReminderDispatch",
                table: "Appointments",
                columns: new[] { "ClinicId", "Id" },
                unique: true);

            migrationBuilder.CreateTable(
                name: "AppointmentReminderDispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentVersionUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReminderLeadHours = table.Column<int>(type: "integer", nullable: false),
                    Purpose = table.Column<int>(type: "integer", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    EligibleAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastStatusCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentReminderDispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppointmentReminderDispatches_Appointments_ClinicId_AppointmentId",
                        columns: x => new { x.ClinicId, x.AppointmentId },
                        principalTable: "Appointments",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AppointmentReminderDispatches_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AutoCheckInPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LeadHours = table.Column<int>(type: "integer", nullable: false),
                    EnableEmail = table.Column<bool>(type: "boolean", nullable: false),
                    EnableSms = table.Column<bool>(type: "boolean", nullable: false),
                    TemplateKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    EligibleVisitTypeIdsJson = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutoCheckInPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AutoCheckInPolicies_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicBusinessHours",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    IsOpen = table.Column<bool>(type: "boolean", nullable: false),
                    StartLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    EndLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    LunchStartLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    LunchEndLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicBusinessHours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicBusinessHours_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicSecurityPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    MfaEnforcementMode = table.Column<int>(type: "integer", nullable: false),
                    MfaEffectiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequirePinChangeOnFirstLogin = table.Column<bool>(type: "boolean", nullable: false),
                    MinimumPinLength = table.Column<int>(type: "integer", nullable: false),
                    SessionInactivityMinutes = table.Column<int>(type: "integer", nullable: false),
                    AllowRoleCustomization = table.Column<bool>(type: "boolean", nullable: false),
                    RestrictCliniciansToOwnSchedules = table.Column<bool>(type: "boolean", nullable: false),
                    AuthorizationMode = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicSecurityPolicies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicSecurityPolicies_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KioskCheckInTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KioskCheckInTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KioskCheckInTokens_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KioskCheckInTokens_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KioskStations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DeviceCredentialHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KioskStations", x => x.Id);
                    table.UniqueConstraint("AK_KioskStations_ClinicId_Id", x => new { x.ClinicId, x.Id });
                    table.ForeignKey(
                        name: "FK_KioskStations_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoleCapabilityPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CapabilityKey = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false),
                    LockedMinimum = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleCapabilityPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleCapabilityPermissions_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleBlockRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicianId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Weekdays = table.Column<int>(type: "integer", nullable: false),
                    StartLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EffectiveStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleBlockRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleBlockRules_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SchedulingPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    DefaultAppointmentDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    AppointmentBufferMinutes = table.Column<int>(type: "integer", nullable: false),
                    AllowDoubleBooking = table.Column<bool>(type: "boolean", nullable: false),
                    AutoConfirmAppointments = table.Column<bool>(type: "boolean", nullable: false),
                    EnableClickToCreate = table.Column<bool>(type: "boolean", nullable: false),
                    ShowIntakeStatus = table.Column<bool>(type: "boolean", nullable: false),
                    AllowCancelFromWeekView = table.Column<bool>(type: "boolean", nullable: false),
                    AllowRescheduleFromWeekView = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultClinicianView = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DefaultAdminView = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IntakeSentColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    IntakeIncompleteColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    IntakeCompleteColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    SendAppointmentReminders = table.Column<bool>(type: "boolean", nullable: false),
                    ReminderLeadHours = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulingPreferences_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserMfaCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedSecret = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastAcceptedTimeStep = table.Column<long>(type: "bigint", nullable: false),
                    FailedAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResetAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResetByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMfaCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMfaCredentials_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VisitTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    RequiresIntake = table.Column<bool>(type: "boolean", nullable: false),
                    PtaAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    IsBillable = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitTypes", x => x.Id);
                    table.UniqueConstraint("AK_VisitTypes_ClinicId_Id", x => new { x.ClinicId, x.Id });
                    table.ForeignKey(
                        name: "FK_VisitTypes_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KioskEnrollmentCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClinicId = table.Column<Guid>(type: "uuid", nullable: false),
                    KioskStationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KioskEnrollmentCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KioskEnrollmentCodes_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KioskEnrollmentCodes_KioskStations_ClinicId_KioskStationId",
                        columns: x => new { x.ClinicId, x.KioskStationId },
                        principalTable: "KioskStations",
                        principalColumns: new[] { "ClinicId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserMfaRecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserMfaCredentialId = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMfaRecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMfaRecoveryCodes_UserMfaCredentials_UserMfaCredentialId",
                        column: x => x.UserMfaCredentialId,
                        principalTable: "UserMfaCredentials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_ClinicId_VisitTypeId",
                table: "Appointments",
                columns: new[] { "ClinicId", "VisitTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminderDispatches_AppointmentId",
                table: "AppointmentReminderDispatches",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminderDispatches_ClinicId",
                table: "AppointmentReminderDispatches",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminderDispatches_IdempotencyKey",
                table: "AppointmentReminderDispatches",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentReminderDispatches_Status_NextAttemptAtUtc",
                table: "AppointmentReminderDispatches",
                columns: new[] { "Status", "NextAttemptAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AutoCheckInPolicies_ClinicId",
                table: "AutoCheckInPolicies",
                column: "ClinicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicBusinessHours_ClinicId_DayOfWeek",
                table: "ClinicBusinessHours",
                columns: new[] { "ClinicId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicSecurityPolicies_ClinicId",
                table: "ClinicSecurityPolicies",
                column: "ClinicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KioskCheckInTokens_AppointmentId",
                table: "KioskCheckInTokens",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_KioskCheckInTokens_ClinicId",
                table: "KioskCheckInTokens",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_KioskCheckInTokens_TokenHash",
                table: "KioskCheckInTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KioskEnrollmentCodes_CodeHash",
                table: "KioskEnrollmentCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KioskEnrollmentCodes_ClinicId_KioskStationId",
                table: "KioskEnrollmentCodes",
                columns: new[] { "ClinicId", "KioskStationId" });

            migrationBuilder.CreateIndex(
                name: "IX_KioskStations_ClinicId_Name",
                table: "KioskStations",
                columns: new[] { "ClinicId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleCapabilityPermissions_ClinicId_RoleKey_CapabilityKey",
                table: "RoleCapabilityPermissions",
                columns: new[] { "ClinicId", "RoleKey", "CapabilityKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleBlockRules_ClinicId_ClinicianId",
                table: "ScheduleBlockRules",
                columns: new[] { "ClinicId", "ClinicianId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleBlockRules_ClinicId_IsActive",
                table: "ScheduleBlockRules",
                columns: new[] { "ClinicId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingPreferences_ClinicId",
                table: "SchedulingPreferences",
                column: "ClinicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserMfaCredentials_UserId",
                table: "UserMfaCredentials",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserMfaRecoveryCodes_UserMfaCredentialId_CodeHash",
                table: "UserMfaRecoveryCodes",
                columns: new[] { "UserMfaCredentialId", "CodeHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitTypes_ClinicId_Code",
                table: "VisitTypes",
                columns: new[] { "ClinicId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitTypes_ClinicId_IsActive_DisplayOrder",
                table: "VisitTypes",
                columns: new[] { "ClinicId", "IsActive", "DisplayOrder" });

            migrationBuilder.Sql(
                """
                INSERT INTO "ClinicSecurityPolicies"
                    ("Id", "ClinicId", "MfaEnforcementMode", "MfaEffectiveAtUtc", "RequirePinChangeOnFirstLogin",
                     "MinimumPinLength", "SessionInactivityMinutes", "AllowRoleCustomization",
                     "RestrictCliniciansToOwnSchedules", "AuthorizationMode", "Version", "UpdatedByUserId",
                     "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), c."Id", 0, NULL, TRUE, 8, 15, TRUE, FALSE, 0, 1,
                       '00000000-0000-0000-0000-000000000001'::uuid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM "Clinics" c
                WHERE NOT EXISTS (SELECT 1 FROM "ClinicSecurityPolicies" p WHERE p."ClinicId" = c."Id");

                INSERT INTO "SchedulingPreferences"
                    ("Id", "ClinicId", "DefaultAppointmentDurationMinutes", "AppointmentBufferMinutes",
                     "AllowDoubleBooking", "AutoConfirmAppointments", "EnableClickToCreate", "ShowIntakeStatus",
                     "AllowCancelFromWeekView", "AllowRescheduleFromWeekView", "DefaultClinicianView", "DefaultAdminView",
                     "IntakeSentColor", "IntakeIncompleteColor", "IntakeCompleteColor", "SendAppointmentReminders",
                     "ReminderLeadHours", "Version", "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), c."Id", 45, 15, FALSE, TRUE, TRUE, TRUE, TRUE, TRUE,
                       'Week', 'AllDay', NULL, NULL, NULL, TRUE, 24, 1,
                       '00000000-0000-0000-0000-000000000001'::uuid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM "Clinics" c
                WHERE NOT EXISTS (SELECT 1 FROM "SchedulingPreferences" p WHERE p."ClinicId" = c."Id");

                INSERT INTO "AutoCheckInPolicies"
                    ("Id", "ClinicId", "IsEnabled", "LeadHours", "EnableEmail", "EnableSms", "TemplateKey",
                     "MaxAttempts", "EligibleVisitTypeIdsJson", "Version", "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), c."Id", FALSE, 24, TRUE, TRUE, 'default-intake-invite', 3, '[]', 1,
                       '00000000-0000-0000-0000-000000000001'::uuid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM "Clinics" c
                WHERE NOT EXISTS (SELECT 1 FROM "AutoCheckInPolicies" p WHERE p."ClinicId" = c."Id");

                INSERT INTO "ClinicBusinessHours"
                    ("Id", "ClinicId", "DayOfWeek", "IsOpen", "StartLocalTime", "EndLocalTime",
                     "LunchStartLocalTime", "LunchEndLocalTime", "Version", "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), c."Id", d."DayOfWeek", d."IsOpen",
                       CASE WHEN d."IsOpen" THEN '08:00'::time END,
                       CASE WHEN d."IsOpen" THEN '17:00'::time END,
                       CASE WHEN d."IsOpen" THEN '12:00'::time END,
                       CASE WHEN d."IsOpen" THEN '13:00'::time END,
                       1, '00000000-0000-0000-0000-000000000001'::uuid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM "Clinics" c
                CROSS JOIN (VALUES (0, FALSE), (1, TRUE), (2, TRUE), (3, TRUE), (4, TRUE), (5, TRUE), (6, FALSE)) d("DayOfWeek", "IsOpen")
                WHERE NOT EXISTS (
                    SELECT 1 FROM "ClinicBusinessHours" h
                    WHERE h."ClinicId" = c."Id" AND h."DayOfWeek" = d."DayOfWeek");

                INSERT INTO "VisitTypes"
                    ("Id", "ClinicId", "Code", "Name", "DurationMinutes", "RequiresIntake", "PtaAllowed",
                     "IsBillable", "IsActive", "DisplayOrder", "Version", "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), c."Id", v."Code", v."Name", v."DurationMinutes", v."RequiresIntake", v."PtaAllowed",
                       v."IsBillable", TRUE, v."DisplayOrder", 1,
                       '00000000-0000-0000-0000-000000000001'::uuid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM "Clinics" c
                CROSS JOIN (VALUES
                    ('initial-evaluation', 'Initial Evaluation', 60, TRUE, FALSE, TRUE, 1),
                    ('re-evaluation', 'Re-Evaluation', 60, FALSE, FALSE, TRUE, 2),
                    ('daily-treatment', 'Daily Treatment', 45, FALSE, TRUE, TRUE, 3),
                    ('progress-note', 'Progress Note', 45, FALSE, TRUE, TRUE, 4),
                    ('discharge', 'Discharge', 30, FALSE, FALSE, TRUE, 5),
                    ('follow-up', 'Follow-Up', 30, FALSE, TRUE, TRUE, 6),
                    ('group-therapy', 'Group Therapy', 60, FALSE, TRUE, TRUE, 7),
                    ('dry-needling', 'Dry Needling', 30, FALSE, FALSE, TRUE, 8),
                    ('telehealth-visit', 'Telehealth Visit', 30, FALSE, TRUE, TRUE, 9),
                    ('home-health-visit', 'Home Health Visit', 60, FALSE, TRUE, TRUE, 10),
                    ('consultation-non-billable', 'Consultation (Non-Billable)', 15, FALSE, FALSE, FALSE, 11),
                    ('no-show', 'No Show', 0, FALSE, FALSE, FALSE, 12))
                    v("Code", "Name", "DurationMinutes", "RequiresIntake", "PtaAllowed", "IsBillable", "DisplayOrder")
                WHERE NOT EXISTS (
                    SELECT 1 FROM "VisitTypes" existing
                    WHERE existing."ClinicId" = c."Id" AND existing."Code" = v."Code");

                INSERT INTO "RoleCapabilityPermissions"
                    ("Id", "ClinicId", "RoleKey", "CapabilityKey", "Level", "LockedMinimum", "Version",
                     "UpdatedByUserId", "CreatedAtUtc", "UpdatedAtUtc")
                SELECT gen_random_uuid(), c."Id", r."RoleKey", p."CapabilityKey",
                       CASE
                           WHEN p."CapabilityKey" NOT IN (1,2,3,5,6,8,9,10,11,12,13,14,19,24,25,26,27,28,30) THEN 0
                           WHEN r."RoleKey" = 'Admin' AND p."CapabilityKey" IN (8,9,10,11,12,25,26,27,28,30) THEN 3
                           WHEN r."RoleKey" = 'Admin' AND p."CapabilityKey" IN (1,13,24) THEN 1
                           WHEN r."RoleKey" = 'Owner' AND p."CapabilityKey" IN (1,8,9,13,24,28) THEN 1
                           WHEN r."RoleKey" = 'PracticeManager' AND p."CapabilityKey" IN (10,11,12,26,28,30) THEN 3
                           WHEN r."RoleKey" = 'PracticeManager' AND p."CapabilityKey" IN (8,9,13,24) THEN 1
                           WHEN r."RoleKey" = 'PT' AND p."CapabilityKey" IN (5,6) THEN 3
                           WHEN r."RoleKey" = 'PT' AND p."CapabilityKey" IN (2,3,10,11,14,19) THEN 2
                           WHEN r."RoleKey" = 'PT' AND p."CapabilityKey" IN (1,8,9,13) THEN 1
                           WHEN r."RoleKey" = 'PTA' AND p."CapabilityKey" = 5 THEN 3
                           WHEN r."RoleKey" = 'PTA' AND p."CapabilityKey" IN (2,3,10,11,14,19) THEN 2
                           WHEN r."RoleKey" = 'PTA' AND p."CapabilityKey" IN (1,8,9,13) THEN 1
                           WHEN r."RoleKey" = 'Aide' AND p."CapabilityKey" IN (8,9) THEN 1
                           WHEN r."RoleKey" = 'FrontDesk' AND p."CapabilityKey" IN (10,11,19) THEN 2
                           WHEN r."RoleKey" = 'FrontDesk' AND p."CapabilityKey" IN (9,13) THEN 1
                           WHEN r."RoleKey" = 'Billing' AND p."CapabilityKey" = 14 THEN 2
                           WHEN r."RoleKey" = 'Billing' AND p."CapabilityKey" IN (1,13) THEN 1
                           ELSE 0
                       END,
                       CASE WHEN r."RoleKey" = 'Admin' AND p."CapabilityKey" IN (26,27) THEN 3 ELSE 0 END,
                       1, '00000000-0000-0000-0000-000000000001'::uuid, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                FROM "Clinics" c
                CROSS JOIN (VALUES ('Admin'), ('Owner'), ('PracticeManager'), ('PT'), ('PTA'), ('Aide'), ('FrontDesk'), ('Billing'), ('Patient')) r("RoleKey")
                CROSS JOIN generate_series(1, 30) p("CapabilityKey")
                WHERE NOT EXISTS (
                    SELECT 1 FROM "RoleCapabilityPermissions" existing
                    WHERE existing."ClinicId" = c."Id" AND existing."RoleKey" = r."RoleKey"
                      AND existing."CapabilityKey" = p."CapabilityKey");

                UPDATE "Appointments" a
                SET "VisitTypeId" = v."Id"
                FROM "VisitTypes" v
                WHERE v."ClinicId" = a."ClinicId"
                  AND v."Code" = CASE a."AppointmentType"
                      WHEN 0 THEN 'initial-evaluation'
                      WHEN 1 THEN 'follow-up'
                      WHEN 2 THEN 'discharge'
                      WHEN 3 THEN 're-evaluation'
                  END
                  AND a."VisitTypeId" IS NULL AND a."AppointmentType" IN (0, 1, 2, 3);
                """);

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION "PreventAppointmentOverlap"()
                RETURNS trigger
                AS $$
                BEGIN
                    IF NEW."Status" IN (5, 6) OR NEW."AuthorizedOverlap" THEN
                        RETURN NEW;
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Appointments" existing
                        WHERE existing."ClinicalId" = NEW."ClinicalId" AND existing."Id" <> NEW."Id"
                          AND existing."Status" NOT IN (5, 6)
                          AND existing."StartTimeUtc" < NEW."EndTimeUtc"
                          AND NEW."StartTimeUtc" < existing."EndTimeUtc") THEN
                        RAISE EXCEPTION 'APPOINTMENT_OVERBOOKING: clinician already has an overlapping appointment';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_VisitTypes_ClinicId_VisitTypeId",
                table: "Appointments",
                columns: new[] { "ClinicId", "VisitTypeId" },
                principalTable: "VisitTypes",
                principalColumns: new[] { "ClinicId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION "PreventAppointmentOverlap"()
                RETURNS trigger
                AS $$
                BEGIN
                    IF NEW."Status" IN (5, 6) THEN
                        RETURN NEW;
                    END IF;
                    IF EXISTS (
                        SELECT 1 FROM "Appointments" existing
                        WHERE existing."ClinicalId" = NEW."ClinicalId" AND existing."Id" <> NEW."Id"
                          AND existing."Status" NOT IN (5, 6)
                          AND existing."StartTimeUtc" < NEW."EndTimeUtc"
                          AND NEW."StartTimeUtc" < existing."EndTimeUtc") THEN
                        RAISE EXCEPTION 'APPOINTMENT_OVERBOOKING: clinician already has an overlapping appointment';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_VisitTypes_ClinicId_VisitTypeId",
                table: "Appointments");

            migrationBuilder.DropTable(
                name: "AppointmentReminderDispatches");

            migrationBuilder.DropIndex(
                name: "UX_Appointments_ClinicId_Id_ReminderDispatch",
                table: "Appointments");

            migrationBuilder.DropTable(
                name: "AutoCheckInPolicies");

            migrationBuilder.DropTable(
                name: "ClinicBusinessHours");

            migrationBuilder.DropTable(
                name: "ClinicSecurityPolicies");

            migrationBuilder.DropTable(
                name: "KioskCheckInTokens");

            migrationBuilder.DropTable(
                name: "KioskEnrollmentCodes");

            migrationBuilder.DropTable(
                name: "RoleCapabilityPermissions");

            migrationBuilder.DropTable(
                name: "ScheduleBlockRules");

            migrationBuilder.DropTable(
                name: "SchedulingPreferences");

            migrationBuilder.DropTable(
                name: "UserMfaRecoveryCodes");

            migrationBuilder.DropTable(
                name: "VisitTypes");

            migrationBuilder.DropTable(
                name: "KioskStations");

            migrationBuilder.DropTable(
                name: "UserMfaCredentials");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_ClinicId_VisitTypeId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "LegacyPinGraceEndsAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MustChangePin",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PinChangedAtUtc",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "AuthorizedOverlap",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "VisitTypeId",
                table: "Appointments");
        }
    }
}
