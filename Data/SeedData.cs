using Microsoft.AspNetCore.Identity;
using WebApplication1.Models;

namespace WebApplication1.Data
{
    public static class SeedData
    {
        public static async Task InitializeAsync(IServiceProvider serviceProvider)
        {
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            string[] roles = { "Student", "Staff" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            string staffEmail = "suchi@iubat.edu";
            var existingStaff = await userManager.FindByEmailAsync(staffEmail);
            if (existingStaff == null)
            {
                var staffUser = new ApplicationUser
                {
                    UserName = staffEmail,
                    Email = staffEmail,
                    FirstName = "Suchi",
                    LastName = "IUBAT",
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(staffUser, "Passw0rd");
                if (result.Succeeded)
                {
                    var roleResult = await userManager.AddToRoleAsync(staffUser, "Staff");
                    Console.WriteLine(roleResult.Succeeded
                        ? $"[Seed] Created staff {staffEmail} and assigned Staff role."
                        : $"[Seed] Created {staffEmail} but failed to add Staff role: {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
                }
                else
                {
                    Console.WriteLine($"[Seed] Failed to create {staffEmail}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }
            else
            {
                // Ensure existing suchi has Staff role (fixes case where user existed without role)
                if (!await userManager.IsInRoleAsync(existingStaff, "Staff"))
                {
                    var addRoleResult = await userManager.AddToRoleAsync(existingStaff, "Staff");
                    Console.WriteLine(addRoleResult.Succeeded
                        ? $"[Seed] Fixed: Added missing Staff role to existing {staffEmail}."
                        : $"[Seed] Failed to add Staff role to {staffEmail}: {string.Join(", ", addRoleResult.Errors.Select(e => e.Description))}");
                }
                else
                {
                    Console.WriteLine($"[Seed] Staff {staffEmail} already exists with Staff role.");
                }
            }

            // Cleanup legacy staff accounts (replaced by suchi@iubat.edu per latest requirement)
            string[] legacyEmails = { "maya@iubat.com", "priya@iubat.edu", "maya@iubat.edu", "arnika@iubat.edu" };
            foreach (var legacyEmail in legacyEmails)
            {
                var legacyUser = await userManager.FindByEmailAsync(legacyEmail);
                if (legacyUser != null)
                {
                    var delResult = await userManager.DeleteAsync(legacyUser);
                    Console.WriteLine(delResult.Succeeded
                        ? $"[Seed] Removed legacy staff {legacyEmail} (migrated to {staffEmail})."
                        : $"[Seed] Failed to remove legacy {legacyEmail}: {string.Join(", ", delResult.Errors.Select(e => e.Description))}");
                }
            }
        }
    }
}
