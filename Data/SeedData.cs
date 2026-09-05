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
                // Ensure existing suchi is fully correct: EmailConfirmed, UserName, Staff role, and password == Passw0rd
                bool needsUpdate = false;
                if (!existingStaff.EmailConfirmed)
                {
                    existingStaff.EmailConfirmed = true;
                    needsUpdate = true;
                }
                if (existingStaff.UserName != staffEmail)
                {
                    existingStaff.UserName = staffEmail;
                    existingStaff.NormalizedUserName = staffEmail.ToUpperInvariant();
                    needsUpdate = true;
                }
                if (needsUpdate)
                {
                    var updResult = await userManager.UpdateAsync(existingStaff);
                    Console.WriteLine(updResult.Succeeded
                        ? $"[Seed] Fixed: Updated profile for {staffEmail} (confirmed/username)."
                        : $"[Seed] Failed to update {staffEmail}: {string.Join(", ", updResult.Errors.Select(e => e.Description))}");
                }

                // Ensure Staff role
                if (!await userManager.IsInRoleAsync(existingStaff, "Staff"))
                {
                    var addRoleResult = await userManager.AddToRoleAsync(existingStaff, "Staff");
                    Console.WriteLine(addRoleResult.Succeeded
                        ? $"[Seed] Fixed: Added missing Staff role to existing {staffEmail}."
                        : $"[Seed] Failed to add Staff role to {staffEmail}: {string.Join(", ", addRoleResult.Errors.Select(e => e.Description))}");
                }

                // Ensure password is exactly Passw0rd — fixes Invalid login attempt when DB had old hash
                const string desiredPassword = "Passw0rd";
                var passwordValid = await userManager.CheckPasswordAsync(existingStaff, desiredPassword);
                if (!passwordValid)
                {
                    var resetToken = await userManager.GeneratePasswordResetTokenAsync(existingStaff);
                    var resetResult = await userManager.ResetPasswordAsync(existingStaff, resetToken, desiredPassword);
                    Console.WriteLine(resetResult.Succeeded
                        ? $"[Seed] Fixed: Reset password for {staffEmail} to desired value."
                        : $"[Seed] Failed to reset password for {staffEmail}: {string.Join(", ", resetResult.Errors.Select(e => e.Description))}");
                    // If reset fails (e.g., token provider issue), fallback: remove + add password
                    if (!resetResult.Succeeded)
                    {
                        var removeResult = await userManager.RemovePasswordAsync(existingStaff);
                        if (removeResult.Succeeded)
                        {
                            var addResult = await userManager.AddPasswordAsync(existingStaff, desiredPassword);
                            Console.WriteLine(addResult.Succeeded
                                ? $"[Seed] Fallback: Set password for {staffEmail} via AddPassword."
                                : $"[Seed] Fallback failed to set password for {staffEmail}: {string.Join(", ", addResult.Errors.Select(e => e.Description))}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"[Seed] Staff {staffEmail} already exists with Staff role and correct password.");
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
