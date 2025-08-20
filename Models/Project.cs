using System;

namespace MVArchive.Models
{
    public class Project
    {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public string? JobNumber { get; set; }
        public string? JobDescription { get; set; }
        public string? JobAddress { get; set; }
        public string? JobPhone { get; set; }
        public string? JobEMail { get; set; }
        public string? JobFax { get; set; }
        public string? ProjectManager { get; set; }
        public string? Architect { get; set; }
        public string? Contractor { get; set; }
        public string? Draftsman { get; set; }
        public string? Estimator { get; set; }
        public string? GeneralContact { get; set; }
        public int? ProjectNumber { get; set; }
        public float? ProjectBudget { get; set; }
        public float? TotalProjectCost { get; set; }
        public DateTime? ScheduledStartDate { get; set; }
        public DateTime? ScheduledCompletionDate { get; set; }
        public DateTime? DateCreated { get; set; }
        public DateTime? DateLastOpened { get; set; }
        public bool? IsInactive { get; set; }
        public bool? PrintFlag { get; set; }
        public bool? Modified { get; set; }
        public int? Type { get; set; }
        public string? LinkID { get; set; }
        public string? LinkIDCategory { get; set; }
        public string? LinkIDCustomerCompany { get; set; }
        public string? LocationCoordinates { get; set; }
    }
}
