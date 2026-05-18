// =============================================================================
// Employee_Leave_Portal — Department ViewModels
// File: ViewModels/DepartmentViewModels.cs
// =============================================================================

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Employee_Leave_Portal.ViewModels
{
    public class DepartmentRowVm
    {
        public int Id { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public string HODName { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }
    }

    public class DepartmentFormVm
    {
        public int Id { get; set; }

        [Required, MaxLength(150)]
        [Display(Name = "Department Name")]
        public string DepartmentName { get; set; } = string.Empty;

        [Display(Name = "Head of Department")]
        public int HOD_EmployeeId { get; set; }

        public List<SelectListItem> HODOptions { get; set; } = new();
    }
}