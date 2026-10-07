using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Baba.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Payroll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    NameAr = table.Column<string>(type: "TEXT", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    JobTitle = table.Column<string>(type: "TEXT", nullable: true),
                    NationalId = table.Column<string>(type: "TEXT", nullable: true),
                    IsNational = table.Column<bool>(type: "INTEGER", nullable: false),
                    JoinDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LeaveDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    BasicSalaryScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    BankName = table.Column<string>(type: "TEXT", nullable: true),
                    BankAccount = table.Column<string>(type: "TEXT", nullable: true),
                    CostCenterId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AnnualLeaveDays = table.Column<int>(type: "INTEGER", nullable: false),
                    LeaveBalanceDaysScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    LeaveBalanceDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Employees_CostCenters_CostCenterId",
                        column: x => x.CostCenterId,
                        principalTable: "CostCenters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EndOfServiceAccruals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AmountScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndOfServiceAccruals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Month = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Memo = table.Column<string>(type: "TEXT", nullable: true),
                    VoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PaymentVoucherId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PaidDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PayrollSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartMonth = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    AutoPost = table.Column<bool>(type: "INTEGER", nullable: false),
                    NationalEmployeePercentScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    NationalEmployerPercentScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    ForeignEmployeePercentScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    ForeignEmployerPercentScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    InsuranceFloorScaled = table.Column<long>(type: "INTEGER", nullable: true),
                    InsuranceCeilingScaled = table.Column<long>(type: "INTEGER", nullable: true),
                    SalaryExpenseAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SalariesPayableAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InsuranceExpenseAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    InsurancePayableAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EndOfServiceExpenseAccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    EndOfServiceProvisionAccountId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayrollSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayrollSettings_Accounts_EndOfServiceExpenseAccountId",
                        column: x => x.EndOfServiceExpenseAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollSettings_Accounts_EndOfServiceProvisionAccountId",
                        column: x => x.EndOfServiceProvisionAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollSettings_Accounts_InsuranceExpenseAccountId",
                        column: x => x.InsuranceExpenseAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollSettings_Accounts_InsurancePayableAccountId",
                        column: x => x.InsurancePayableAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollSettings_Accounts_SalariesPayableAccountId",
                        column: x => x.SalariesPayableAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayrollSettings_Accounts_SalaryExpenseAccountId",
                        column: x => x.SalaryExpenseAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalaryComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    NameAr = table.Column<string>(type: "TEXT", nullable: false),
                    NameEn = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Calculation = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultValueScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsInsurable = table.Column<bool>(type: "INTEGER", nullable: false),
                    InEndOfService = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalaryComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalaryComponents_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LeaveRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    From = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    To = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DaysScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeaveRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LeaveRecords_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payslips",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeInsuranceScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    EmployerInsuranceScaled = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payslips", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payslips_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Payslips_PayrollRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PayrollRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeComponents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComponentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ValueScaled = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmployeeComponents_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EmployeeComponents_SalaryComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "SalaryComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayslipItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayslipId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LineNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    LabelAr = table.Column<string>(type: "TEXT", nullable: false),
                    ComponentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AmountScaled = table.Column<long>(type: "INTEGER", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsInsurable = table.Column<bool>(type: "INTEGER", nullable: false),
                    InEndOfService = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsBasic = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayslipItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PayslipItems_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayslipItems_Payslips_PayslipId",
                        column: x => x.PayslipId,
                        principalTable: "Payslips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PayslipItems_SalaryComponents_ComponentId",
                        column: x => x.ComponentId,
                        principalTable: "SalaryComponents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeComponents_ComponentId",
                table: "EmployeeComponents",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeComponents_EmployeeId",
                table: "EmployeeComponents",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CompanyId_Code",
                table: "Employees",
                columns: new[] { "CompanyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Employees_CostCenterId",
                table: "Employees",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_EndOfServiceAccruals_CompanyId_Month",
                table: "EndOfServiceAccruals",
                columns: new[] { "CompanyId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LeaveRecords_EmployeeId",
                table: "LeaveRecords",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollRuns_CompanyId_Month",
                table: "PayrollRuns",
                columns: new[] { "CompanyId", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSettings_EndOfServiceExpenseAccountId",
                table: "PayrollSettings",
                column: "EndOfServiceExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSettings_EndOfServiceProvisionAccountId",
                table: "PayrollSettings",
                column: "EndOfServiceProvisionAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSettings_InsuranceExpenseAccountId",
                table: "PayrollSettings",
                column: "InsuranceExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSettings_InsurancePayableAccountId",
                table: "PayrollSettings",
                column: "InsurancePayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSettings_SalariesPayableAccountId",
                table: "PayrollSettings",
                column: "SalariesPayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayrollSettings_SalaryExpenseAccountId",
                table: "PayrollSettings",
                column: "SalaryExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayslipItems_AccountId",
                table: "PayslipItems",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PayslipItems_ComponentId",
                table: "PayslipItems",
                column: "ComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_PayslipItems_PayslipId",
                table: "PayslipItems",
                column: "PayslipId");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_EmployeeId",
                table: "Payslips",
                column: "EmployeeId");

            migrationBuilder.CreateIndex(
                name: "IX_Payslips_RunId",
                table: "Payslips",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryComponents_AccountId",
                table: "SalaryComponents",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_SalaryComponents_CompanyId_Code",
                table: "SalaryComponents",
                columns: new[] { "CompanyId", "Code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmployeeComponents");

            migrationBuilder.DropTable(
                name: "EndOfServiceAccruals");

            migrationBuilder.DropTable(
                name: "LeaveRecords");

            migrationBuilder.DropTable(
                name: "PayrollSettings");

            migrationBuilder.DropTable(
                name: "PayslipItems");

            migrationBuilder.DropTable(
                name: "Payslips");

            migrationBuilder.DropTable(
                name: "SalaryComponents");

            migrationBuilder.DropTable(
                name: "Employees");

            migrationBuilder.DropTable(
                name: "PayrollRuns");
        }
    }
}
