using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using OnionProcOparetor.Server.Data;

namespace OnionProcOparetor.Server.Migrations
{
    [DbContext(typeof(AppDbContext))]
    partial class AppDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder.HasAnnotation("ProductVersion", "10.0.0");

            modelBuilder.Entity("OnionProcOparetor.Server.Models.Rule", b =>
            {
                b.Property<int>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("INTEGER");

                b.Property<DateTime>("CreatedAt")
                    .HasColumnType("TEXT");

                b.Property<string>("Description")
                    .HasColumnType("TEXT");

                b.Property<bool>("Enabled")
                    .HasColumnType("INTEGER");

                b.Property<string>("Name")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<string>("Type")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<string>("Value")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.HasKey("Id");

                b.ToTable("Rules");
            });

            modelBuilder.Entity("OnionProcOparetor.Server.Models.PollResponse", b =>
            {
                b.Property<int>("Id")
                    .ValueGeneratedOnAdd()
                    .HasColumnType("INTEGER");

                b.Property<string>("DeviceName")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.Property<string>("IpAddress")
                    .HasColumnType("TEXT");

                b.Property<string>("Message")
                    .HasColumnType("TEXT");

                b.Property<string>("RuleName")
                    .HasColumnType("TEXT");

                b.Property<DateTime>("Timestamp")
                    .HasColumnType("TEXT");

                b.Property<string>("UserName")
                    .IsRequired()
                    .HasColumnType("TEXT");

                b.HasKey("Id");

                b.ToTable("PollResponses");
            });
#pragma warning restore 612, 618
        }
    }
}
