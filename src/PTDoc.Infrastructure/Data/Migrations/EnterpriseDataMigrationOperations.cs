using Microsoft.EntityFrameworkCore.Migrations;

namespace PTDoc.Infrastructure.Data.Migrations;

public static class EnterpriseDataMigrationOperations
{
    public static void Up(
        MigrationBuilder migrationBuilder,
        string npiNotNullFilter,
        string activeInsurancePolicyFilter,
        string activeNoteTemplateFilter,
        bool supportsAlterTableForeignKeys = true)
    {
        migrationBuilder.CreateTable("ProviderDirectoryEntries",table=>new
        {
            Id=table.Column<Guid>(nullable:false),ClinicId=table.Column<Guid>(nullable:true),FirstName=table.Column<string>(maxLength:100,nullable:false),LastName=table.Column<string>(maxLength:100,nullable:false),Credentials=table.Column<string>(maxLength:50,nullable:true),Npi=table.Column<string>(maxLength:10,nullable:true),Specialty=table.Column<string>(maxLength:150,nullable:true),TaxonomyCode=table.Column<string>(maxLength:20,nullable:true),OrganizationName=table.Column<string>(maxLength:200,nullable:true),Phone=table.Column<string>(maxLength:30,nullable:true),Fax=table.Column<string>(maxLength:30,nullable:true),Email=table.Column<string>(maxLength:255,nullable:true),AddressLine1=table.Column<string>(maxLength:200,nullable:true),AddressLine2=table.Column<string>(maxLength:200,nullable:true),City=table.Column<string>(maxLength:100,nullable:true),State=table.Column<string>(maxLength:100,nullable:true),ZipCode=table.Column<string>(maxLength:20,nullable:true),Status=table.Column<int>(nullable:false),SubmissionSource=table.Column<int>(nullable:false),SubmittedByUserId=table.Column<Guid>(nullable:true),ReviewedByUserId=table.Column<Guid>(nullable:true),SubmittedAtUtc=table.Column<DateTime>(nullable:false),ReviewedAtUtc=table.Column<DateTime>(nullable:true),ReviewReason=table.Column<string>(maxLength:500,nullable:true),IsArchived=table.Column<bool>(nullable:false),LastModifiedUtc=table.Column<DateTime>(nullable:false),ModifiedByUserId=table.Column<Guid>(nullable:false),SyncState=table.Column<int>(nullable:false)
        },constraints:table=>{table.PrimaryKey("PK_ProviderDirectoryEntries",x=>x.Id);table.ForeignKey("FK_ProviderDirectoryEntries_Clinics_ClinicId",x=>x.ClinicId,"Clinics","Id",onDelete:ReferentialAction.Restrict);});

        migrationBuilder.CreateTable("PatientInsurancePolicies",table=>new
        {
            Id=table.Column<Guid>(nullable:false),PatientId=table.Column<Guid>(nullable:false),ClinicId=table.Column<Guid>(nullable:true),CoveragePriority=table.Column<int>(nullable:false),CarrierKey=table.Column<string>(maxLength:100,nullable:true),CarrierDisplayName=table.Column<string>(maxLength:200,nullable:true),PayerType=table.Column<int>(nullable:false),MemberOrPolicyNumber=table.Column<string>(maxLength:100,nullable:true),GroupNumber=table.Column<string>(maxLength:100,nullable:true),EffectiveStartDate=table.Column<DateTime>(nullable:true),EffectiveEndDate=table.Column<DateTime>(nullable:true),PlanYearType=table.Column<int>(nullable:false),DeductibleAmount=table.Column<decimal>(precision:18,scale:2,nullable:true),DeductibleMet=table.Column<decimal>(precision:18,scale:2,nullable:true),OutOfPocketMaximum=table.Column<decimal>(precision:18,scale:2,nullable:true),OutOfPocketMet=table.Column<decimal>(precision:18,scale:2,nullable:true),CopayAmount=table.Column<decimal>(precision:18,scale:2,nullable:true),CoinsurancePercent=table.Column<decimal>(precision:5,scale:2,nullable:true),AdjusterName=table.Column<string>(maxLength:150,nullable:true),AdjusterPhone=table.Column<string>(maxLength:30,nullable:true),AdjusterEmail=table.Column<string>(maxLength:255,nullable:true),AdjusterFax=table.Column<string>(maxLength:30,nullable:true),Status=table.Column<int>(nullable:false),IsArchived=table.Column<bool>(nullable:false),LastModifiedUtc=table.Column<DateTime>(nullable:false),ModifiedByUserId=table.Column<Guid>(nullable:false),SyncState=table.Column<int>(nullable:false)
        },constraints:table=>{table.PrimaryKey("PK_PatientInsurancePolicies",x=>x.Id);table.ForeignKey("FK_PatientInsurancePolicies_Clinics_ClinicId",x=>x.ClinicId,"Clinics","Id",onDelete:ReferentialAction.Restrict);table.ForeignKey("FK_PatientInsurancePolicies_Patients_PatientId",x=>x.PatientId,"Patients","Id",onDelete:ReferentialAction.Cascade);});

        migrationBuilder.CreateTable("NoteTemplates",table=>new
        {
            Id=table.Column<Guid>(nullable:false),ClinicId=table.Column<Guid>(nullable:true),NoteType=table.Column<int>(nullable:false),Variant=table.Column<int>(nullable:false),Name=table.Column<string>(maxLength:150,nullable:false),ActiveVersionId=table.Column<Guid>(nullable:true),IsArchived=table.Column<bool>(nullable:false),CreatedAtUtc=table.Column<DateTime>(nullable:false),LastModifiedUtc=table.Column<DateTime>(nullable:false),CreatedByUserId=table.Column<Guid>(nullable:false),ModifiedByUserId=table.Column<Guid>(nullable:false)
        },constraints:table=>{table.PrimaryKey("PK_NoteTemplates",x=>x.Id);table.ForeignKey("FK_NoteTemplates_Clinics_ClinicId",x=>x.ClinicId,"Clinics","Id",onDelete:ReferentialAction.Restrict);});

        migrationBuilder.CreateTable("PatientProviderRelationships",table=>new
        {
            Id=table.Column<Guid>(nullable:false),PatientId=table.Column<Guid>(nullable:false),ProviderDirectoryEntryId=table.Column<Guid>(nullable:false),ClinicId=table.Column<Guid>(nullable:true),Role=table.Column<int>(nullable:false),EffectiveStartDate=table.Column<DateTime>(nullable:true),EffectiveEndDate=table.Column<DateTime>(nullable:true),IsPrimary=table.Column<bool>(nullable:false),IsArchived=table.Column<bool>(nullable:false),LastModifiedUtc=table.Column<DateTime>(nullable:false),ModifiedByUserId=table.Column<Guid>(nullable:false),SyncState=table.Column<int>(nullable:false)
        },constraints:table=>{table.PrimaryKey("PK_PatientProviderRelationships",x=>x.Id);table.ForeignKey("FK_PatientProviderRelationships_Clinics_ClinicId",x=>x.ClinicId,"Clinics","Id",onDelete:ReferentialAction.Restrict);table.ForeignKey("FK_PatientProviderRelationships_Patients_PatientId",x=>x.PatientId,"Patients","Id",onDelete:ReferentialAction.Cascade);table.ForeignKey("FK_PatientProviderRelationships_ProviderDirectoryEntries_ProviderDirectoryEntryId",x=>x.ProviderDirectoryEntryId,"ProviderDirectoryEntries","Id",onDelete:ReferentialAction.Restrict);});

        migrationBuilder.CreateTable("PatientInsuranceAuthorizations",table=>new
        {
            Id=table.Column<Guid>(nullable:false),PatientInsurancePolicyId=table.Column<Guid>(nullable:false),PatientId=table.Column<Guid>(nullable:false),ClinicId=table.Column<Guid>(nullable:true),AuthorizationType=table.Column<int>(nullable:false),ReferenceNumber=table.Column<string>(maxLength:100,nullable:true),Status=table.Column<int>(nullable:false),ReceivedDate=table.Column<DateTime>(nullable:true),StartDate=table.Column<DateTime>(nullable:true),EndDate=table.Column<DateTime>(nullable:true),AuthorizedUnits=table.Column<decimal>(precision:18,scale:2,nullable:true),UsedUnits=table.Column<decimal>(precision:18,scale:2,nullable:true),VisitLimitPeriod=table.Column<int>(nullable:false),ReauthorizationDueDate=table.Column<DateTime>(nullable:true),VisitAlertThreshold=table.Column<int>(nullable:true),Notes=table.Column<string>(maxLength:2000,nullable:true),IsArchived=table.Column<bool>(nullable:false),LastModifiedUtc=table.Column<DateTime>(nullable:false),ModifiedByUserId=table.Column<Guid>(nullable:false),SyncState=table.Column<int>(nullable:false)
        },constraints:table=>{table.PrimaryKey("PK_PatientInsuranceAuthorizations",x=>x.Id);table.ForeignKey("FK_PatientInsuranceAuthorizations_Clinics_ClinicId",x=>x.ClinicId,"Clinics","Id",onDelete:ReferentialAction.Restrict);table.ForeignKey("FK_PatientInsuranceAuthorizations_Patients_PatientId",x=>x.PatientId,"Patients","Id",onDelete:ReferentialAction.Restrict);table.ForeignKey("FK_PatientInsuranceAuthorizations_PatientInsurancePolicies_PatientInsurancePolicyId",x=>x.PatientInsurancePolicyId,"PatientInsurancePolicies","Id",onDelete:ReferentialAction.Cascade);});

        migrationBuilder.CreateTable("NoteTemplateVersions",table=>new
        {
            Id=table.Column<Guid>(nullable:false),NoteTemplateId=table.Column<Guid>(nullable:false),ClinicId=table.Column<Guid>(nullable:true),VersionNumber=table.Column<int>(nullable:false),Status=table.Column<int>(nullable:false),SchemaJson=table.Column<string>(nullable:false),CreatedByUserId=table.Column<Guid>(nullable:false),SubmittedByUserId=table.Column<Guid>(nullable:true),ReviewedByUserId=table.Column<Guid>(nullable:true),CreatedAtUtc=table.Column<DateTime>(nullable:false),LastModifiedUtc=table.Column<DateTime>(nullable:false),SubmittedAtUtc=table.Column<DateTime>(nullable:true),PublishedAtUtc=table.Column<DateTime>(nullable:true),RetiredAtUtc=table.Column<DateTime>(nullable:true),ReviewComment=table.Column<string>(maxLength:1000,nullable:true)
        },constraints:table=>{table.PrimaryKey("PK_NoteTemplateVersions",x=>x.Id);table.ForeignKey("FK_NoteTemplateVersions_Clinics_ClinicId",x=>x.ClinicId,"Clinics","Id",onDelete:ReferentialAction.Restrict);table.ForeignKey("FK_NoteTemplateVersions_NoteTemplates_NoteTemplateId",x=>x.NoteTemplateId,"NoteTemplates","Id",onDelete:ReferentialAction.Cascade);});

        migrationBuilder.AddColumn<Guid>("TemplateVersionId","ClinicalNotes",nullable:true);
        if (supportsAlterTableForeignKeys)
        {
            migrationBuilder.AddForeignKey("FK_ClinicalNotes_NoteTemplateVersions_TemplateVersionId","ClinicalNotes","TemplateVersionId","NoteTemplateVersions",principalColumn:"Id",onDelete:ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey("FK_NoteTemplates_NoteTemplateVersions_ActiveVersionId","NoteTemplates","ActiveVersionId","NoteTemplateVersions",principalColumn:"Id",onDelete:ReferentialAction.Restrict);
        }

        migrationBuilder.CreateIndex("IX_ProviderDirectoryEntries_ClinicId_Npi","ProviderDirectoryEntries",new[]{"ClinicId","Npi"},unique:true,filter:npiNotNullFilter);
        migrationBuilder.CreateIndex("IX_ProviderDirectoryEntries_ClinicId_Status_LastName_FirstName","ProviderDirectoryEntries",new[]{"ClinicId","Status","LastName","FirstName"});
        migrationBuilder.CreateIndex("IX_PatientProviderRelationships_ClinicId_ProviderDirectoryEntryId","PatientProviderRelationships",new[]{"ClinicId","ProviderDirectoryEntryId"});migrationBuilder.CreateIndex("IX_PatientProviderRelationships_PatientId_Role_IsArchived","PatientProviderRelationships",new[]{"PatientId","Role","IsArchived"});migrationBuilder.CreateIndex("IX_PatientProviderRelationships_ProviderDirectoryEntryId","PatientProviderRelationships","ProviderDirectoryEntryId");
        migrationBuilder.CreateIndex("IX_PatientInsurancePolicies_ClinicId_PatientId","PatientInsurancePolicies",new[]{"ClinicId","PatientId"});migrationBuilder.CreateIndex("UX_PatientInsurancePolicies_PatientId_CoveragePriority_Active","PatientInsurancePolicies",new[]{"PatientId","CoveragePriority"},unique:true,filter:activeInsurancePolicyFilter);
        migrationBuilder.CreateIndex("IX_PatientInsuranceAuthorizations_ClinicId_PatientId","PatientInsuranceAuthorizations",new[]{"ClinicId","PatientId"});migrationBuilder.CreateIndex("IX_PatientInsuranceAuthorizations_PatientInsurancePolicyId_IsArchived","PatientInsuranceAuthorizations",new[]{"PatientInsurancePolicyId","IsArchived"});migrationBuilder.CreateIndex("IX_PatientInsuranceAuthorizations_PatientId","PatientInsuranceAuthorizations","PatientId");
        migrationBuilder.CreateIndex("IX_NoteTemplates_ActiveVersionId","NoteTemplates","ActiveVersionId");migrationBuilder.CreateIndex("UX_NoteTemplates_ClinicId_NoteType_Variant_Active","NoteTemplates",new[]{"ClinicId","NoteType","Variant"},unique:true,filter:activeNoteTemplateFilter);
        migrationBuilder.CreateIndex("IX_NoteTemplateVersions_ClinicId_Status","NoteTemplateVersions",new[]{"ClinicId","Status"});migrationBuilder.CreateIndex("IX_NoteTemplateVersions_NoteTemplateId_VersionNumber","NoteTemplateVersions",new[]{"NoteTemplateId","VersionNumber"},unique:true);
        migrationBuilder.CreateIndex("IX_ClinicalNotes_TemplateVersionId","ClinicalNotes","TemplateVersionId");
    }

    public static void Down(MigrationBuilder migrationBuilder, bool supportsAlterTableForeignKeys = true)
    {
        if (supportsAlterTableForeignKeys)
            migrationBuilder.DropForeignKey("FK_ClinicalNotes_NoteTemplateVersions_TemplateVersionId","ClinicalNotes");
        migrationBuilder.DropIndex("IX_ClinicalNotes_TemplateVersionId","ClinicalNotes");migrationBuilder.DropColumn("TemplateVersionId","ClinicalNotes");
        migrationBuilder.DropTable("PatientInsuranceAuthorizations");migrationBuilder.DropTable("PatientProviderRelationships");migrationBuilder.DropTable("PatientInsurancePolicies");
        if (supportsAlterTableForeignKeys)
            migrationBuilder.DropForeignKey("FK_NoteTemplates_NoteTemplateVersions_ActiveVersionId","NoteTemplates");
        migrationBuilder.DropTable("NoteTemplateVersions");migrationBuilder.DropTable("NoteTemplates");migrationBuilder.DropTable("ProviderDirectoryEntries");
    }
}
