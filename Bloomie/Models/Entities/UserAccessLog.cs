using Bloomie.Data;

namespace Bloomie.Models.Entities
{
    public class UserAccessLog
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public DateTime AccessTime { get; set; }
        public ApplicationUser User { get; set; }
        public string Url { get; set; }
    }
}