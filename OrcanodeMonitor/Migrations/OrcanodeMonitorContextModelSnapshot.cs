﻿// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OrcanodeMonitor.Data;

#nullable disable

namespace OrcanodeMonitor.Migrations
{
    [DbContext(typeof(OrcanodeMonitorContext))]
    partial class OrcanodeMonitorContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.8")
                .HasAnnotation("Proxies:ChangeTracking", false)
                .HasAnnotation("Proxies:CheckEquality", false)
                .HasAnnotation("Proxies:LazyLoading", true)
                .HasAnnotation("Relational:MaxIdentifierLength", 128);

            SqlServerModelBuilderExtensions.UseIdentityColumns(modelBuilder);

            modelBuilder.Entity("OrcanodeMonitor.Models.MonitorState", b =>
                {
                    b.Property<int>("ID")
                        .HasColumnType("int");

                    b.Property<DateTime?>("LastUpdatedTimestampUtc")
                        .HasColumnType("datetime2");

                    b.HasKey("ID");

                    b.ToTable("MonitorState", (string)null);
                });

            modelBuilder.Entity("OrcanodeMonitor.Models.Orcanode", b =>
                {
                    b.Property<int>("ID")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("ID"));

                    b.Property<string>("AgentVersion")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<double?>("AudioStandardDeviation")
                        .HasColumnType("float");

                    b.Property<string>("DataplicityDescription")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("DataplicityName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool?>("DataplicityOnline")
                        .HasColumnType("bit");

                    b.Property<string>("DataplicitySerial")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool?>("DataplicityUpgradeAvailable")
                        .HasColumnType("bit");

                    b.Property<long>("DiskCapacity")
                        .HasColumnType("bigint");

                    b.Property<long>("DiskUsed")
                        .HasColumnType("bigint");

                    b.Property<DateTime?>("LastCheckedUtc")
                        .HasColumnType("datetime2");

                    b.Property<DateTime?>("LatestRecordedUtc")
                        .HasColumnType("datetime2");

                    b.Property<DateTime?>("LatestUploadedUtc")
                        .HasColumnType("datetime2");

                    b.Property<DateTime?>("ManifestUpdatedUtc")
                        .HasColumnType("datetime2");

                    b.Property<string>("OrcaHelloId")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("OrcasoundFeedId")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("OrcasoundName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("OrcasoundSlug")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<bool?>("OrcasoundVisible")
                        .HasColumnType("bit");

                    b.Property<string>("S3Bucket")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("S3NodeName")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("ID");

                    b.ToTable("Orcanode", (string)null);
                });

            modelBuilder.Entity("OrcanodeMonitor.Models.OrcanodeEvent", b =>
                {
                    b.Property<int>("ID")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("int");

                    SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<int>("ID"));

                    b.Property<DateTime>("DateTimeUtc")
                        .HasColumnType("datetime2");

                    b.Property<int>("OrcanodeId")
                        .HasColumnType("int");

                    b.Property<string>("Slug")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Type")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.Property<string>("Value")
                        .IsRequired()
                        .HasColumnType("nvarchar(max)");

                    b.HasKey("ID");

                    b.HasIndex("OrcanodeId");

                    b.ToTable("OrcanodeEvent", (string)null);
                });

            modelBuilder.Entity("OrcanodeMonitor.Models.OrcanodeEvent", b =>
                {
                    b.HasOne("OrcanodeMonitor.Models.Orcanode", "Orcanode")
                        .WithMany()
                        .HasForeignKey("OrcanodeId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("Orcanode");
                });
#pragma warning restore 612, 618
        }
    }
}
