# Duplicate Groups

## Group 1: GRP-40-047e610e

**Extractability:** extractable  
**Score:** 43.94  
**Occurrences:** 3  

**Preview:**

```csharp
migrationBuilder.DropTable(name: "accom_pricing_cache");
migrationBuilder.DropTable(name: "admin_notes");
migrationBuilder.DropTable(name: "affiliate_activity_logs");
migrationBuilder.DropTable(name: "affiliate_applications");
migrationBuilder.DropTable(name: "api_call_logs");
migrationBuilder.DropTable(name: "audit_logs");
migrationBuilder.DropTable(name: "auth_sessions");
migrationBuilder.DropTable(name: "badges");
migrationBuilder.DropTable(name: "blog_image_sources");
migrationBuilder.DropTable(name: "blog_posts");
migrationBuilder.DropTable(name: "blog_themes");
migrationBuilder.DropTable(name: "campervan_listings");
migrationBuilder.DropTable(name: "card_orders");
migrationBuilder.DropTable(name: "combo_products");
migrationBuilder.DropTable(name: "combos");
migrationBuilder.DropTable(name: "contact_submissions");
migrationBuilder.DropTable(name: "email_logs");
migrationBuilder.DropTable(name: "email_templates");
migrationBuilder.DropTable(name: "embed_widgets");
migrationBuilder.DropTable(name: "hero_availability_cache");
migrationBuilder.DropTable(name: "hero_cache_status");
migrationBuilder.DropTable(name: "hero_request_logs");
migrationBuilder.DropTable(name: "keywords");
migrationBuilder.DropTable(name: "locations");
migrationBuilder.DropTable(name: "password_reset_tokens");
migrationBuilder.DropTable(name: "payments");
migrationBuilder.DropTable(name: "product_media");
migrationBuilder.DropTable(name: "products");
migrationBuilder.DropTable(name: "promo_codes");
migrationBuilder.DropTable(name: "quotes");
migrationBuilder.DropTable(name: "sections");
migrationBuilder.DropTable(name: "sessions");
migrationBuilder.DropTable(name: "settings");
migrationBuilder.DropTable(name: "shared_itineraries");
migrationBuilder.DropTable(name: "supplier_agreements");
migrationBuilder.DropTable(name: "supplier_branches");
migrationBuilder.DropTable(name: "supplier_product_approvals");
migrationBuilder.DropTable(name: "supplier_product_comments");
migrationBuilder.DropTable(name: "supplier_team_members");
migrationBuilder.DropTable(name: "suppliers");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.cs:1301" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.cs#L1301">20260402021243_InitialCreate.cs:1301-1419</a> | InitialCreate | Down |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.cs:1304" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.cs#L1304">20260402021243_InitialCreate.cs:1304-1422</a> | InitialCreate | Down |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.cs:1307" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.cs#L1307">20260402021243_InitialCreate.cs:1307-1425</a> | InitialCreate | Down |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 2: GRP-24-cd60d2a7

**Extractability:** extractable  
**Score:** 16.64  
**Occurrences:** 2  

**Preview:**

```csharp
modelBuilder.Entity("TourStore.Domain.Models.AuditLog", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Action").IsRequired().HasColumnType("text").HasColumnName("action");
    b.Property<JsonElement?>("Changes").HasColumnType("jsonb").HasColumnName("changes");
    b.Property<DateTime>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("EntityId").HasColumnType("text").HasColumnName("entity_id");
    b.Property<string>("EntityName").HasColumnType("text").HasColumnName("entity_name");
    b.Property<string>("EntityType").IsRequired().HasColumnType("text").HasColumnName("entity_type");
    b.Property<string>("IpAddress").HasColumnType("text").HasColumnName("ip_address");
    b.Property<string>("UserAgent").HasColumnType("text").HasColumnName("user_agent");
    b.Property<string>("UserEmail").HasColumnType("text").HasColumnName("user_email");
    b.Property<string>("UserId").HasColumnType("text").HasColumnName("user_id");
    b.HasKey("Id");
    b.ToTable("audit_logs");
});
modelBuilder.Entity("TourStore.Domain.Models.AuthSession", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<DateTime>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<DateTime>("ExpiresAt").HasColumnType("timestamp with time zone").HasColumnName("expires_at");
    b.Property<string>("IpAddress").HasColumnType("text").HasColumnName("ip_address");
    b.Property<bool>("Revoked").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("revoked");
    b.Property<string>("SessionToken").IsRequired().HasColumnType("text").HasColumnName("session_token");
    b.Property<string>("UserAgent").HasColumnType("text").HasColumnName("user_agent");
    b.Property<string>("UserId").IsRequired().HasColumnType("text").HasColumnName("user_id");
    b.HasKey("Id");
    b.HasIndex("SessionToken").IsUnique();
    b.HasIndex("UserId");
    b.ToTable("auth_sessions");
});
modelBuilder.Entity("TourStore.Domain.Models.Badge", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Color").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("bg-emerald-600").HasColumnName("color");
    b.Property<string>("Description").HasColumnType("text").HasColumnName("description");
    b.Property<bool?>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<int?>("SortOrder").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("sort_order");
    b.HasKey("Id");
    b.ToTable("badges");
});
modelBuilder.Entity("TourStore.Domain.Models.BlogImageSource", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("BlogPostId").HasColumnType("text").HasColumnName("blog_post_id");
    b.Property<string>("BlogPostTitle").HasColumnType("text").HasColumnName("blog_post_title");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("ImageUrl").IsRequired().HasColumnType("text").HasColumnName("image_url");
    b.Property<string>("SearchQuery").HasColumnType("text").HasColumnName("search_query");
    b.Property<bool>("Selected").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("selected");
    b.Property<string>("SourcePage").HasColumnType("text").HasColumnName("source_page");
    b.Property<string>("SourceTitle").HasColumnType("text").HasColumnName("source_title");
    b.Property<string>("ThumbnailUrl").HasColumnType("text").HasColumnName("thumbnail_url");
    b.HasKey("Id");
    b.ToTable("blog_image_sources");
});
modelBuilder.Entity("TourStore.Domain.Models.BlogPost", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Content").IsRequired().HasColumnType("text").HasColumnName("content");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<List<string>>("FeaturedProductIds").HasColumnType("text[]").HasColumnName("featured_product_ids");
    b.Property<List<string>>("FeaturedSupplierIds").HasColumnType("text[]").HasColumnName("featured_supplier_ids");
    b.Property<string>("Image").HasColumnType("text").HasColumnName("image");
    b.Property<List<string>>("Keywords").HasColumnType("text[]").HasColumnName("keywords");
    b.Property<string>("LocationId").HasColumnType("text").HasColumnName("location_id");
    b.Property<List<string>>("ReferenceUrls").HasColumnType("text[]").HasColumnName("reference_urls");
    b.Property<string>("Slug").IsRequired().HasColumnType("text").HasColumnName("slug");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("draft").HasColumnName("status");
    b.Property<string>("Summary").IsRequired().HasColumnType("text").HasColumnName("summary");
    b.Property<int?>("TargetLength").HasColumnType("integer").HasColumnName("target_length");
    b.Property<string>("ThemeId").HasColumnType("text").HasColumnName("theme_id");
    b.Property<string>("Title").IsRequired().HasColumnType("text").HasColumnName("title");
    b.Property<DateTime?>("UpdatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("updated_at").HasDefaultValueSql("now()");
    b.HasKey("Id");
    b.ToTable("blog_posts");
});
modelBuilder.Entity("TourStore.Domain.Models.BlogTheme", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<bool?>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<List<string>>("Keywords").HasColumnType("text[]").HasColumnName("keywords");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("ProductFilter").HasColumnType("text").HasColumnName("product_filter");
    b.Property<List<string>>("ReferenceUrls").HasColumnType("text[]").HasColumnName("reference_urls");
    b.Property<string>("SummaryTemplate").IsRequired().HasColumnType("text").HasColumnName("summary_template");
    b.Property<int?>("TargetLength").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(800).HasColumnName("target_length");
    b.Property<string>("TitleTemplate").IsRequired().HasColumnType("text").HasColumnName("title_template");
    b.HasKey("Id");
    b.ToTable("blog_themes");
});
modelBuilder.Entity("TourStore.Domain.Models.CampervanListing", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<JsonElement?>("Amenities").HasColumnType("jsonb").HasColumnName("amenities");
    b.Property<int?>("Berths").HasColumnType("integer").HasColumnName("berths");
    b.Property<string>("BodyStyle").HasColumnType("text").HasColumnName("body_style");
    b.Property<string>("BookingTerms").HasColumnType("text").HasColumnName("booking_terms");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("DriveType").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("2WD").HasColumnName("drive_type");
    b.Property<List<string>>("DropoffLocations").HasColumnType("text[]").HasColumnName("dropoff_locations");
    b.Property<decimal?>("ExcessAmount").HasColumnType("numeric").HasColumnName("excess_amount");
    b.Property<JsonElement?>("Extras").HasColumnType("jsonb").HasColumnName("extras");
    b.Property<bool?>("Featured").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("featured");
    b.Property<bool?>("FreeCampingCapable").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("free_camping_capable");
    b.Property<string>("FuelEconomy").HasColumnType("text").HasColumnName("fuel_economy");
    b.Property<string>("FuelTankCapacity").HasColumnType("text").HasColumnName("fuel_tank_capacity");
    b.Property<string>("FuelType").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Petrol").HasColumnName("fuel_type");
    b.Property<string>("HeroProductId").HasColumnType("text").HasColumnName("hero_product_id");
    b.Property<string>("HeroProductName").HasColumnType("text").HasColumnName("hero_product_name");
    b.Property<List<string>>("Highlights").HasColumnType("text[]").HasColumnName("highlights");
    b.Property<string>("Image").HasColumnType("text").HasColumnName("image");
    b.Property<string>("Image2").HasColumnType("text").HasColumnName("image2");
    b.Property<string>("Image3").HasColumnType("text").HasColumnName("image3");
    b.Property<string>("Image4").HasColumnType("text").HasColumnName("image4");
    b.Property<string>("Image5").HasColumnType("text").HasColumnName("image5");
    b.Property<string>("Image6").HasColumnType("text").HasColumnName("image6");
    b.Property<string>("Image7").HasColumnType("text").HasColumnName("image7");
    b.Property<string>("Image8").HasColumnType("text").HasColumnName("image8");
    b.Property<List<string>>("ImportantInfo").HasColumnType("text[]").HasColumnName("important_info");
    b.Property<List<string>>("Included").HasColumnType("text[]").HasColumnName("included");
    b.Property<string>("InsuranceInfo").HasColumnType("text").HasColumnName("insurance_info");
    b.Property<string>("LicenseRequirements").HasColumnType("text").HasColumnName("license_requirements");
    b.Property<string>("LongDescription").HasColumnType("text").HasColumnName("long_description");
    b.Property<string>("MakeModel").HasColumnType("text").HasColumnName("make_model");
    b.Property<string>("MileagePolicy").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Unlimited").HasColumnName("mileage_policy");
    b.Property<int?>("MinDriverAge").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(21).HasColumnName("min_driver_age");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<List<string>>("NotIncluded").HasColumnType("text[]").HasColumnName("not_included");
    b.Property<bool?>("OneWayAvailable").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("one_way_available");
    b.Property<bool?>("PetFriendly").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("pet_friendly");
    b.Property<List<string>>("PickupLocations").HasColumnType("text[]").HasColumnName("pickup_locations");
    b.Property<string>("PriceCurrency").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("AUD").HasColumnName("price_currency");
    b.Property<decimal?>("PriceFrom").HasColumnType("numeric").HasColumnName("price_from");
    b.Property<bool?>("RoadsideAssistance").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("roadside_assistance");
    b.Property<int?>("Seatbelts").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(2).HasColumnName("seatbelts");
    b.Property<string>("ShortDescription").HasColumnType("text").HasColumnName("short_description");
    b.Property<string>("SizeLength").HasColumnType("text").HasColumnName("size_length");
    b.Property<int?>("Sleeps").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(2).HasColumnName("sleeps");
    b.Property<string>("Slug").HasColumnType("text").HasColumnName("slug");
    b.Property<int?>("SortOrder").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("sort_order");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Draft").HasColumnName("status");
    b.Property<string>("SupplierId").IsRequired().HasColumnType("text").HasColumnName("supplier_id");
    b.Property<bool?>("TowHitch").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("tow_hitch");
    b.Property<string>("Transmission").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Automatic").HasColumnName("transmission");
    b.Property<DateTime?>("UpdatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("updated_at").HasDefaultValueSql("now()");
    b.Property<string>("VehicleType").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Campervan").HasColumnName("vehicle_type");
    b.Property<string>("YearAge").HasColumnType("text").HasColumnName("year_age");
    b.HasKey("Id");
    b.ToTable("campervan_listings");
});
modelBuilder.Entity("TourStore.Domain.Models.CardOrder", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("AddressLine1").IsRequired().HasColumnType("text").HasColumnName("address_line1");
    b.Property<string>("AddressLine2").HasColumnType("text").HasColumnName("address_line2");
    b.Property<string>("AffiliateEmail").IsRequired().HasColumnType("text").HasColumnName("affiliate_email");
    b.Property<string>("AffiliateId").IsRequired().HasColumnType("text").HasColumnName("affiliate_id");
    b.Property<string>("AffiliateName").IsRequired().HasColumnType("text").HasColumnName("affiliate_name");
    b.Property<string>("AffiliatePromoCode").HasColumnType("text").HasColumnName("affiliate_promo_code");
    b.Property<string>("CardType").IsRequired().HasColumnType("text").HasColumnName("card_type");
    b.Property<string>("City").IsRequired().HasColumnType("text").HasColumnName("city");
    b.Property<string>("Country").IsRequired().HasColumnType("text").HasColumnName("country");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Notes").HasColumnType("text").HasColumnName("notes");
    b.Property<string>("PostalCode").IsRequired().HasColumnType("text").HasColumnName("postal_code");
    b.Property<DateTime?>("PostedAt").HasColumnType("timestamp with time zone").HasColumnName("posted_at");
    b.Property<decimal?>("Price").ValueGeneratedOnAdd().HasColumnType("numeric").HasDefaultValue(0m).HasColumnName("price");
    b.Property<int>("Quantity").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(1).HasColumnName("quantity");
    b.Property<string>("State").HasColumnType("text").HasColumnName("state");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("pending").HasColumnName("status");
    b.HasKey("Id");
    b.ToTable("card_orders");
});
modelBuilder.Entity("TourStore.Domain.Models.Combo", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("BenefitText").HasColumnType("text").HasColumnName("benefit_text");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("DiscountType").IsRequired().HasColumnType("text").HasColumnName("discount_type");
    b.Property<decimal>("DiscountValue").HasColumnType("numeric").HasColumnName("discount_value");
    b.Property<string>("FeaturedImage").HasColumnType("text").HasColumnName("featured_image");
    b.Property<List<string>>("Highlights").HasColumnType("text[]").HasColumnName("highlights");
    b.Property<bool>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<string>("LocationId").IsRequired().HasColumnType("text").HasColumnName("location_id");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("ShortDescription").HasColumnType("text").HasColumnName("short_description");
    b.Property<bool>("ShowDiscountAtCheckout").HasColumnType("boolean").HasColumnName("show_discount_at_checkout");
    b.Property<string>("Slug").IsRequired().HasColumnType("text").HasColumnName("slug");
    b.Property<int?>("SortPriority").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("sort_priority");
    b.Property<DateTime?>("UpdatedAt").HasColumnType("timestamp with time zone").HasColumnName("updated_at");
    b.HasKey("Id");
    b.ToTable("combos");
});
modelBuilder.Entity("TourStore.Domain.Models.ComboProduct", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("ComboId").IsRequired().HasColumnType("text").HasColumnName("combo_id");
    b.Property<string>("ProductId").IsRequired().HasColumnType("text").HasColumnName("product_id");
    b.Property<int?>("SortOrder").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("sort_order");
    b.HasKey("Id");
    b.ToTable("combo_products");
});
modelBuilder.Entity("TourStore.Domain.Models.ContactSubmission", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Email").IsRequired().HasColumnType("text").HasColumnName("email");
    b.Property<string>("Message").IsRequired().HasColumnType("text").HasColumnName("message");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("Notes").HasColumnType("text").HasColumnName("notes");
    b.Property<string>("Phone").HasColumnType("text").HasColumnName("phone");
    b.Property<string>("ProductName").HasColumnType("text").HasColumnName("product_name");
    b.Property<DateTime?>("RespondedAt").HasColumnType("timestamp with time zone").HasColumnName("responded_at");
    b.Property<string>("RespondedBy").HasColumnType("text").HasColumnName("responded_by");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("new").HasColumnName("status");
    b.Property<string>("SupplierName").HasColumnType("text").HasColumnName("supplier_name");
    b.Property<string>("Type").IsRequired().HasColumnType("text").HasColumnName("type");
    b.HasKey("Id");
    b.ToTable("contact_submissions");
});
modelBuilder.Entity("TourStore.Domain.Models.EmailLog", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("ErrorMessage").HasColumnType("text").HasColumnName("error_message");
    b.Property<string>("ItineraryId").HasColumnType("text").HasColumnName("itinerary_id");
    b.Property<JsonElement?>("Metadata").HasColumnType("jsonb").HasColumnName("metadata");
    b.Property<string>("RecipientEmail").IsRequired().HasColumnType("text").HasColumnName("recipient_email");
    b.Property<string>("RecipientName").HasColumnType("text").HasColumnName("recipient_name");
    b.Property<string>("ResendId").HasColumnType("text").HasColumnName("resend_id");
    b.Property<DateTime?>("SentAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("sent_at").HasDefaultValueSql("now()");
    b.Property<string>("SentBy").HasColumnType("text").HasColumnName("sent_by");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("sent").HasColumnName("status");
    b.Property<string>("Subject").IsRequired().HasColumnType("text").HasColumnName("subject");
    b.Property<string>("TemplateName").HasColumnType("text").HasColumnName("template_name");
    b.Property<string>("TemplateSlug").HasColumnType("text").HasColumnName("template_slug");
    b.HasKey("Id");
    b.ToTable("email_logs");
});
modelBuilder.Entity("TourStore.Domain.Models.EmailTemplate", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Description").HasColumnType("text").HasColumnName("description");
    b.Property<string>("HtmlBody").IsRequired().HasColumnType("text").HasColumnName("html_body");
    b.Property<bool>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("Slug").IsRequired().HasColumnType("text").HasColumnName("slug");
    b.Property<string>("Subject").IsRequired().HasColumnType("text").HasColumnName("subject");
    b.Property<DateTime?>("UpdatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("updated_at").HasDefaultValueSql("now()");
    b.Property<JsonElement?>("Variables").HasColumnType("jsonb").HasColumnName("variables");
    b.HasKey("Id");
    b.HasIndex("Slug").IsUnique();
    b.ToTable("email_templates");
});
modelBuilder.Entity("TourStore.Domain.Models.EmbedWidget", b =>
{
    b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("id");
    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
    b.Property<string>("AccentColor").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("#0ea5e9").HasColumnName("accent_color");
    b.Property<string>("AffiliateId").HasColumnType("text").HasColumnName("affiliate_id");
    b.Property<List<string>>("AllowedDomains").HasColumnType("text[]").HasColumnName("allowed_domains");
    b.Property<bool?>("Archived").HasColumnType("boolean").HasColumnName("archived");
    b.Property<string>("BackgroundColor").HasColumnType("text").HasColumnName("background_color");
    b.Property<int?>("Clicks").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("clicks");
    b.Property<string>("Code").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("code");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<decimal?>("DiscountPercent").HasColumnType("numeric").HasColumnName("discount_percent");
    b.Property<string>("DiscountType").HasColumnType("text").HasColumnName("discount_type");
    b.Property<string>("DisplayStyle").HasMaxLength(20).HasColumnType("character varying(20)").HasColumnName("display_style");
    b.Property<string>("HeadingColor").HasColumnType("text").HasColumnName("heading_color");
    b.Property<string>("HeadingText").HasColumnType("text").HasColumnName("heading_text");
    b.Property<string>("Height").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("600").HasColumnName("height");
    b.Property<int?>("Impressions").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("impressions");
    b.Property<bool?>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<string>("LayoutStyle").ValueGeneratedOnAdd().HasMaxLength(20).HasColumnType("character varying(20)").HasDefaultValue("vertical").HasColumnName("layout_style");
    b.Property<int?>("LocationId").HasColumnType("integer").HasColumnName("location_id");
    b.Property<string>("LocationName").HasColumnType("text").HasColumnName("location_name");
    b.Property<string>("ProductId").HasColumnType("text").HasColumnName("product_id");
    b.Property<string>("ProductName").HasColumnType("text").HasColumnName("product_name");
    b.Property<string>("SecondaryPromoCode").HasColumnType("text").HasColumnName("secondary_promo_code");
    b.Property<bool?>("SecondaryPromoEnabled").HasColumnType("boolean").HasColumnName("secondary_promo_enabled");
    b.Property<string>("SubtitleColor").HasColumnType("text").HasColumnName("subtitle_color");
    b.Property<string>("SubtitleText").HasColumnType("text").HasColumnName("subtitle_text");
    b.Property<string>("SuccessRedirectUrl").HasColumnType("text").HasColumnName("success_redirect_url");
    b.Property<string>("SupplierId").HasColumnType("text").HasColumnName("supplier_id");
    b.Property<string>("SupplierName").HasColumnType("text").HasColumnName("supplier_name");
    b.Property<string>("WidgetType").IsRequired().HasColumnType("text").HasColumnName("widget_type");
    b.Property<string>("Width").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("100%").HasColumnName("width");
    b.HasKey("Id");
    b.HasIndex("Code").IsUnique();
    b.ToTable("embed_widgets");
});
modelBuilder.Entity("TourStore.Domain.Models.HeroAvailabilityCache", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<bool>("Available").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("available");
    b.Property<decimal?>("BasePrice").HasColumnType("numeric").HasColumnName("base_price");
    b.Property<string>("Currency").HasColumnType("text").HasColumnName("currency");
    b.Property<string>("Date").IsRequired().HasColumnType("text").HasColumnName("date");
    b.Property<DateTime?>("ExpiresAt").HasColumnType("timestamp with time zone").HasColumnName("expires_at");
    b.Property<JsonElement?>("FareTypesData").HasColumnType("jsonb").HasColumnName("fare_types");
    b.Property<string>("HeroProductId").IsRequired().HasColumnType("text").HasColumnName("hero_product_id");
    b.Property<DateTime?>("LastUpdated").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("fetched_at").HasDefaultValueSql("now()");
    b.Property<string>("ProductId").HasColumnType("text").HasColumnName("product_id");
    b.Property<JsonElement?>("SessionsData").HasColumnType("jsonb").HasColumnName("sessions");
    b.Property<int>("TotalAvailability").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("total_availability");
    b.HasKey("Id");
    b.HasIndex("HeroProductId", "Date").IsUnique();
    b.ToTable("hero_availability_cache");
});
modelBuilder.Entity("TourStore.Domain.Models.HeroCacheStatus", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("CacheStatus").HasColumnType("text").HasColumnName("last_refresh_status");
    b.Property<int>("DaysLoaded").HasColumnType("integer").HasColumnName("days_loaded");
    b.Property<string>("ErrorMessage").HasColumnType("text").HasColumnName("last_error");
    b.Property<string>("HeroProductId").IsRequired().HasColumnType("text").HasColumnName("hero_product_id");
    b.Property<DateTime?>("LastCacheTime").HasColumnType("timestamp with time zone").HasColumnName("last_refresh_at");
    b.Property<DateTime?>("LastPricingRefreshAt").HasColumnType("timestamp with time zone").HasColumnName("last_pricing_refresh_at");
    b.Property<DateTime?>("NextRefreshAt").HasColumnType("timestamp with time zone").HasColumnName("next_refresh_at");
    b.Property<string>("ProductId").HasColumnType("text").HasColumnName("product_id");
    b.Property<string>("ProductName").HasColumnType("text").HasColumnName("product_title");
    b.HasKey("Id");
    b.ToTable("hero_cache_status");
});
modelBuilder.Entity("TourStore.Domain.Models.HeroRequestLog", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("ChangeType").IsRequired().HasColumnType("text").HasColumnName("change_type");
    b.Property<DateTime?>("CompletedAt").HasColumnType("timestamp with time zone").HasColumnName("completed_at");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<DateTime?>("EmailSentAt").HasColumnType("timestamp with time zone").HasColumnName("email_sent_at");
    b.Property<string>("FieldName").IsRequired().HasColumnType("text").HasColumnName("field_name");
    b.Property<string>("NewValue").HasColumnType("text").HasColumnName("new_value");
    b.Property<string>("Notes").HasColumnType("text").HasColumnName("notes");
    b.Property<string>("OldValue").HasColumnType("text").HasColumnName("old_value");
    b.Property<string>("RequestedBy").HasColumnType("text").HasColumnName("requested_by");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("pending").HasColumnName("status");
    b.Property<string>("SupplierId").IsRequired().HasColumnType("text").HasColumnName("supplier_id");
    b.Property<string>("SupplierName").IsRequired().HasColumnType("text").HasColumnName("supplier_name");
    b.HasKey("Id");
    b.ToTable("hero_request_logs");
});
modelBuilder.Entity("TourStore.Domain.Models.Keyword", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Category").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Activities").HasColumnName("category");
    b.Property<bool?>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("ParentKeywordId").HasColumnType("text").HasColumnName("parent_keyword_id");
    b.HasKey("Id");
    b.ToTable("keywords");
});
modelBuilder.Entity("TourStore.Domain.Models.Location", b =>
{
    b.Property<string>("Id").HasColumnType("text").HasColumnName("id");
    b.Property<string>("AccomBgImage").HasColumnType("text").HasColumnName("accom_bg_image");
    b.Property<string>("AccomBgVideo").HasColumnType("text").HasColumnName("accom_bg_video");
    b.Property<float?>("AccomFilmGrain").HasColumnType("real").HasColumnName("accom_film_grain");
    b.Property<string>("AccomOverlayColor").HasColumnType("text").HasColumnName("accom_overlay_color");
    b.Property<float?>("AccomOverlayOpacity").HasColumnType("real").HasColumnName("accom_overlay_opacity");
    b.Property<float?>("AccomVideoEnd").HasColumnType("real").HasColumnName("accom_video_end");
    b.Property<float?>("AccomVideoSpeed").HasColumnType("real").HasColumnName("accom_video_speed");
    b.Property<float?>("AccomVideoStart").HasColumnType("real").HasColumnName("accom_video_start");
    b.Property<bool?>("AccomWidgetCarousel").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("accom_widget_carousel");
    b.Property<bool?>("AccommodationEnabled").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("accommodation_enabled");
    b.Property<bool?>("AllowLocationSwitch").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("allow_location_switch");
    b.Property<string>("CampervanBgImage").HasColumnType("text").HasColumnName("campervan_bg_image");
    b.Property<string>("CampervanBgVideo").HasColumnType("text").HasColumnName("campervan_bg_video");
    b.Property<bool?>("CampervanEnabled").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("campervan_enabled");
    b.Property<float?>("CampervanFilmGrain").HasColumnType("real").HasColumnName("campervan_film_grain");
    b.Property<string>("CampervanOverlayColor").HasColumnType("text").HasColumnName("campervan_overlay_color");
    b.Property<float?>("CampervanOverlayOpacity").HasColumnType("real").HasColumnName("campervan_overlay_opacity");
    b.Property<float?>("CampervanVideoEnd").HasColumnType("real").HasColumnName("campervan_video_end");
    b.Property<float?>("CampervanVideoSpeed").HasColumnType("real").HasColumnName("campervan_video_speed");
    b.Property<float?>("CampervanVideoStart").HasColumnType("real").HasColumnName("campervan_video_start");
    b.Property<string>("Color").HasColumnType("text").HasColumnName("color");
    b.Property<string>("Country").HasColumnType("text").HasColumnName("country");
    b.Property<string>("CountryCode").HasColumnType("text").HasColumnName("country_code");
    b.Property<string>("Currency").HasColumnType("text").HasColumnName("currency");
    b.Property<float?>("FilmGrain").HasColumnType("real").HasColumnName("film_grain");
    b.Property<string>("GooglePlaceId").HasColumnType("text").HasColumnName("google_place_id");
    b.Property<string>("HeroApiKey").HasColumnType("text").HasColumnName("hero_api_key");
    b.Property<string>("HeroFont").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Caveat").HasColumnName("hero_font");
    b.Property<string>("HeroImage").HasColumnType("text").HasColumnName("hero_image");
    b.Property<string>("HeroText").HasColumnType("text").HasColumnName("hero_text");
    b.Property<string>("HeroVideo").HasColumnType("text").HasColumnName("hero_video");
    b.Property<string>("HeroVideos").HasColumnType("text").HasColumnName("hero_videos");
    b.Property<string>("Language").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("en-US").HasColumnName("language");
    b.Property<float?>("Latitude").HasColumnType("real").HasColumnName("latitude");
    b.Property<string>("Layout").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("header_image").HasColumnName("layout");
    b.Property<string>("Logo").HasColumnType("text").HasColumnName("logo");
    b.Property<float?>("Longitude").HasColumnType("real").HasColumnName("longitude");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<float?>("OverlayOpacity").ValueGeneratedOnAdd().HasColumnType("real").HasDefaultValue(0.4f).HasColumnName("overlay_opacity");
    b.Property<string>("ParentLocationId").HasColumnType("text").HasColumnName("parent_location_id");
    b.Property<JsonElement?>("PickupAreas").HasColumnType("jsonb").HasColumnName("pickup_areas");
    b.Property<List<string>>("PopularProductIds").HasColumnType("text[]").HasColumnName("popular_product_ids");
    b.Property<bool?>("ProcessingFeeAbsorb").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("processing_fee_absorb");
    b.Property<string>("ProcessingFeeMode").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("separate").HasColumnName("processing_fee_mode");
    b.Property<decimal?>("ProcessingFeePercent").ValueGeneratedOnAdd().HasColumnType("numeric").HasDefaultValue(3m).HasColumnName("processing_fee_percent");
    b.Property<string>("Region").IsRequired().HasColumnType("text").HasColumnName("region");
    b.Property<string>("SecondaryColor").HasColumnType("text").HasColumnName("secondary_color");
    b.Property<string>("SeoDescription").HasColumnType("text").HasColumnName("seo_description");
    b.Property<string>("SeoKeywords").HasColumnType("text").HasColumnName("seo_keywords");
    b.Property<string>("SeoTitle").HasColumnType("text").HasColumnName("seo_title");
    b.Property<string>("ShortDescription").HasColumnType("text").HasColumnName("short_description");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Draft").HasColumnName("status");
    b.Property<string>("TaxDisplayMode").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("exclusive").HasColumnName("tax_display_mode");
    b.Property<float?>("TileFilmGrain").HasColumnType("real").HasColumnName("tile_film_grain");
    b.Property<string>("Timezone").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("America/Los_Angeles").HasColumnName("timezone");
    b.Property<string>("TransportBgImage").HasColumnType("text").HasColumnName("transport_bg_image");
    b.Property<string>("TransportBgVideo").HasColumnType("text").HasColumnName("transport_bg_video");
    b.Property<bool?>("TransportEnabled").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("transport_enabled");
    b.Property<float?>("TransportFilmGrain").HasColumnType("real").HasColumnName("transport_film_grain");
    b.Property<string>("TransportOverlayColor").HasColumnType("text").HasColumnName("transport_overlay_color");
    b.Property<float?>("TransportOverlayOpacity").HasColumnType("real").HasColumnName("transport_overlay_opacity");
    b.Property<float?>("TransportVideoEnd").HasColumnType("real").HasColumnName("transport_video_end");
    b.Property<float?>("TransportVideoSpeed").HasColumnType("real").HasColumnName("transport_video_speed");
    b.Property<float?>("TransportVideoStart").HasColumnType("real").HasColumnName("transport_video_start");
    b.Property<float?>("VideoEndTime").HasColumnType("real").HasColumnName("video_end_time");
    b.Property<float?>("VideoPlaybackSpeed").ValueGeneratedOnAdd().HasColumnType("real").HasDefaultValue(1f).HasColumnName("video_playback_speed");
    b.Property<float?>("VideoStartTime").ValueGeneratedOnAdd().HasColumnType("real").HasDefaultValue(0f).HasColumnName("video_start_time");
    b.HasKey("Id");
    b.ToTable("locations");
});
modelBuilder.Entity("TourStore.Domain.Models.PasswordResetToken", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<DateTime>("ExpiresAt").HasColumnType("timestamp with time zone").HasColumnName("expires_at");
    b.Property<string>("Token").IsRequired().HasColumnType("text").HasColumnName("token");
    b.Property<bool?>("Used").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("used");
    b.Property<string>("UserId").IsRequired().HasColumnType("text").HasColumnName("user_id");
    b.HasKey("Id");
    b.ToTable("password_reset_tokens");
});
modelBuilder.Entity("TourStore.Domain.Models.Payment", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<decimal>("Amount").HasColumnType("numeric").HasColumnName("amount");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Method").IsRequired().HasColumnType("text").HasColumnName("method");
    b.Property<string>("Notes").HasColumnType("text").HasColumnName("notes");
    b.Property<string>("QuoteId").IsRequired().HasColumnType("text").HasColumnName("quote_id");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("pending").HasColumnName("status");
    b.Property<string>("StripePaymentIntentId").HasColumnType("text").HasColumnName("stripe_payment_intent_id");
    b.HasKey("Id");
    b.ToTable("payments");
});
modelBuilder.Entity("TourStore.Domain.Models.Product", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<List<string>>("AdditionalLocationIds").HasColumnType("text[]").HasColumnName("additional_location_ids");
    b.Property<JsonElement?>("Addons").HasColumnType("jsonb").HasColumnName("addons");
    b.Property<int?>("AgeMax").HasColumnType("integer").HasColumnName("age_max");
    b.Property<int?>("AgeMin").HasColumnType("integer").HasColumnName("age_min");
    b.Property<JsonElement?>("Amenities").HasColumnType("jsonb").HasColumnName("amenities");
    b.Property<List<int>>("AvailableDays").HasColumnType("integer[]").HasColumnName("available_days");
    b.Property<List<string>>("Badges").HasColumnType("text[]").HasColumnName("badges");
    b.Property<string>("BranchId").HasColumnType("text").HasColumnName("branch_id");
    b.Property<string>("BranchName").HasColumnType("text").HasColumnName("branch_name").HasAnnotation("Relational:JsonPropertyName", "branchName");
    b.Property<JsonElement>("BringWithYou").HasColumnType("jsonb").HasColumnName("bring_with_you");
    b.Property<decimal?>("CancellationFee1Override").HasColumnType("numeric").HasColumnName("cancellation_fee1_override");
    b.Property<decimal?>("CancellationFee2Override").HasColumnType("numeric").HasColumnName("cancellation_fee2_override");
    b.Property<decimal?>("CancellationFee3Override").HasColumnType("numeric").HasColumnName("cancellation_fee3_override");
    b.Property<string>("CancellationPeriod1Override").HasColumnType("text").HasColumnName("cancellation_period1_override");
    b.Property<string>("CancellationPeriod2Override").HasColumnType("text").HasColumnName("cancellation_period2_override");
    b.Property<string>("CancellationPeriod3Override").HasColumnType("text").HasColumnName("cancellation_period3_override");
    b.Property<string>("CardHighlight").HasColumnType("text").HasColumnName("card_highlight");
    b.Property<JsonElement>("Categories").HasColumnType("jsonb").HasColumnName("categories");
    b.Property<decimal?>("CommissionRate").ValueGeneratedOnAdd().HasColumnType("numeric").HasDefaultValue(10m).HasColumnName("commission_rate");
    b.Property<JsonElement?>("ContentSourceUrls").HasColumnType("jsonb").HasColumnName("content_source_urls");
    b.Property<string>("CoolingType").HasColumnType("text").HasColumnName("cooling_type");
    b.Property<JsonElement?>("DeniedKeywords").HasColumnType("jsonb").HasColumnName("denied_keywords");
    b.Property<string>("DormGender").HasColumnType("text").HasColumnName("dorm_gender");
    b.Property<string>("Duration").IsRequired().HasColumnType("text").HasColumnName("duration");
    b.Property<int?>("DurationDays").HasColumnType("integer").HasColumnName("duration_days");
    b.Property<int?>("DurationHours").HasColumnType("integer").HasColumnName("duration_hours");
    b.Property<int?>("DurationMinutes").HasColumnType("integer").HasColumnName("duration_minutes");
    b.Property<string>("EndLocation").HasColumnType("text").HasColumnName("end_location");
    b.Property<JsonElement?>("ExcludedPickupNames").HasColumnType("jsonb").HasColumnName("excluded_pickup_names");
    b.Property<string>("FacilityType").HasColumnType("text").HasColumnName("facility_type");
    b.Property<JsonElement?>("FareTypes").HasColumnType("jsonb").HasColumnName("fare_types");
    b.Property<bool?>("ForcedAddonBeforePassengers").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("forced_addon_before_passengers");
    b.Property<string>("GroupConfiguration").HasColumnType("text").HasColumnName("group_configuration");
    b.Property<string>("GroupVariantOfId").HasColumnType("text").HasColumnName("group_variant_of_id");
    b.Property<JsonElement?>("HeroAddonOverrides").HasColumnType("jsonb").HasColumnName("hero_addon_overrides");
    b.Property<int?>("HeroCategory").HasColumnType("integer").HasColumnName("hero_category");
    b.Property<string>("HeroProductId").HasColumnType("text").HasColumnName("hero_product_id");
    b.Property<string>("HeroProductName").HasColumnType("text").HasColumnName("hero_product_name");
    b.Property<JsonElement>("Highlights").HasColumnType("jsonb").HasColumnName("highlights");
    b.Property<string>("Image").IsRequired().HasColumnType("text").HasColumnName("image");
    b.Property<JsonElement?>("Images").HasColumnType("jsonb").HasColumnName("images");
    b.Property<JsonElement>("ImportantInfo").HasColumnType("jsonb").HasColumnName("important_info");
    b.Property<JsonElement>("Included").HasColumnType("jsonb").HasColumnName("included");
    b.Property<bool?>("IsAccessible").HasColumnType("boolean").HasColumnName("is_accessible");
    b.Property<JsonElement>("Keywords").HasColumnType("jsonb").HasColumnName("keywords");
    b.Property<decimal?>("Levy").HasColumnType("numeric").HasColumnName("levy");
    b.Property<string>("LevyDescription").HasColumnType("text").HasColumnName("levy_description");
    b.Property<List<string>>("LinkedToProductIds").HasColumnType("text[]").HasColumnName("linked_to_product_ids");
    b.Property<string>("LocationId").IsRequired().HasColumnType("text").HasColumnName("location_id");
    b.Property<string>("LongDescription").IsRequired().HasColumnType("text").HasColumnName("long_description");
    b.Property<int?>("MaxOccupancy").HasColumnType("integer").HasColumnName("max_occupancy");
    b.Property<int?>("MinOccupancy").HasColumnType("integer").HasColumnName("min_occupancy");
    b.Property<int?>("MinPassengers").HasColumnType("integer").HasColumnName("min_passengers");
    b.Property<JsonElement>("NotIncluded").HasColumnType("jsonb").HasColumnName("not_included");
    b.Property<int?>("NumberOfNights").HasColumnType("integer").HasColumnName("number_of_nights");
    b.Property<JsonElement?>("Options").HasColumnType("jsonb").HasColumnName("options");
    b.Property<JsonElement?>("PickupAreaIds").HasColumnType("jsonb").HasColumnName("pickup_area_ids");
    b.Property<string>("PickupLocationExclude").HasColumnType("text").HasColumnName("pickup_location_exclude");
    b.Property<string>("PickupLocationFilter").HasColumnType("text").HasColumnName("pickup_location_filter");
    b.Property<JsonElement?>("PickupLocations").HasColumnType("jsonb").HasColumnName("pickup_locations");
    b.Property<decimal>("Price").HasColumnType("numeric").HasColumnName("price");
    b.Property<string>("PricedPer").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("person").HasColumnName("priced_per");
    b.Property<bool?>("PricedPerBed").HasColumnType("boolean").HasColumnName("priced_per_bed");
    b.Property<string>("PricingType").IsRequired().HasColumnType("text").HasColumnName("pricing_type");
    b.Property<string>("ProductType").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("standalone").HasColumnName("product_type");
    b.Property<JsonElement?>("Quote").HasColumnType("jsonb").HasColumnName("quote");
    b.Property<float>("Rating").HasColumnType("real").HasColumnName("rating");
    b.Property<JsonElement?>("RequiredInfo").HasColumnType("jsonb").HasColumnName("required_info");
    b.Property<bool?>("RequiresSupplierApproval").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("requires_supplier_approval");
    b.Property<int>("Reviews").HasColumnType("integer").HasColumnName("reviews");
    b.Property<string>("RoomFeature").HasColumnType("text").HasColumnName("room_feature");
    b.Property<string>("RoomType").HasColumnType("text").HasColumnName("room_type");
    b.Property<JsonElement?>("RouteStops").HasColumnType("jsonb").HasColumnName("route_stops");
    b.Property<string>("RouteStopsSourceUrl").HasColumnType("text").HasColumnName("route_stops_source_url");
    b.Property<string>("SeoDescription").HasColumnType("text").HasColumnName("seo_description");
    b.Property<string>("SeoKeywords").HasColumnType("text").HasColumnName("seo_keywords");
    b.Property<string>("SeoTitle").HasColumnType("text").HasColumnName("seo_title");
    b.Property<string>("ShortDescription").IsRequired().HasColumnType("text").HasColumnName("short_description");
    b.Property<bool?>("ShowVideoOnPage").HasColumnType("boolean").HasColumnName("show_video_on_page");
    b.Property<bool?>("ShowVideoOnTile").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("show_video_on_tile");
    b.Property<string>("StartLocation").HasColumnType("text").HasColumnName("start_location");
    b.Property<string>("StartTime").HasColumnType("text").HasColumnName("start_time");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Draft").HasColumnName("status");
    b.Property<string>("SupplierId").HasColumnType("text").HasColumnName("supplier_id");
    b.Property<string>("SupplierName").IsRequired().HasColumnType("text").HasColumnName("supplier").HasAnnotation("Relational:JsonPropertyName", "supplier");
    b.Property<string>("SupplierUrl").HasColumnType("text").HasColumnName("supplier_url");
    b.Property<string>("SupplierUrl2").HasColumnType("text").HasColumnName("supplier_url2");
    b.Property<string>("SupplierUrl3").HasColumnType("text").HasColumnName("supplier_url3");
    b.Property<string>("Title").IsRequired().HasColumnType("text").HasColumnName("title");
    b.Property<string>("TransportType").HasColumnType("text").HasColumnName("transport_type");
    b.Property<bool?>("VariantBeforeTimeslot").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("variant_before_timeslot");
    b.Property<string>("VariantGroupId").HasColumnType("text").HasColumnName("variant_group_id");
    b.Property<string>("VariantLabel").HasColumnType("text").HasColumnName("variant_label");
    b.Property<string>("VariantType").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("same_duration").HasColumnName("variant_type");
    b.Property<JsonElement>("Variations").HasColumnType("jsonb").HasColumnName("variations");
    b.Property<float?>("VideoEndTime").HasColumnType("real").HasColumnName("video_end_time");
    b.Property<float?>("VideoPlaybackRate").ValueGeneratedOnAdd().HasColumnType("real").HasDefaultValue(1f).HasColumnName("video_playback_rate");
    b.Property<float?>("VideoStartTime").ValueGeneratedOnAdd().HasColumnType("real").HasDefaultValue(0f).HasColumnName("video_start_time");
    b.Property<string>("YoutubeVideo").HasColumnType("text").HasColumnName("youtube_video");
    b.HasKey("Id");
    b.ToTable("products");
});
modelBuilder.Entity("TourStore.Domain.Models.ProductMedia", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Caption").HasColumnType("text").HasColumnName("caption");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("ProductId").HasColumnType("text").HasColumnName("product_id");
    b.Property<int?>("SortOrder").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("sort_order");
    b.Property<string>("StorageKey").HasColumnType("text").HasColumnName("storage_key");
    b.Property<string>("SupplierId").HasColumnType("text").HasColumnName("supplier_id");
    b.Property<string>("Type").IsRequired().HasColumnType("text").HasColumnName("type");
    b.Property<string>("Url").IsRequired().HasColumnType("text").HasColumnName("url");
    b.HasKey("Id");
    b.ToTable("product_media");
});
modelBuilder.Entity("TourStore.Domain.Models.PromoCode", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("AccessLevel").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("affiliate").HasColumnName("access_level");
    b.Property<string>("AffiliateColor").HasColumnType("text").HasColumnName("affiliate_color");
    b.Property<string>("AffiliateType").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("person").HasColumnName("affiliate_type");
    b.Property<bool?>("AllowLocationSwitchingPos").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("allow_location_switching_pos");
    b.Property<bool?>("AllowLocationSwitchingPublic").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("allow_location_switching_public");
    b.Property<JsonElement?>("ApplicableIds").HasColumnType("jsonb").HasColumnName("applicable_ids");
    b.Property<string>("ApplicableTo").IsRequired().HasColumnType("text").HasColumnName("applicable_to");
    b.Property<bool>("Archived").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("archived");
    b.Property<JsonElement?>("AssignedLocations").HasColumnType("jsonb").HasColumnName("assigned_locations");
    b.Property<JsonElement?>("BankDetails").HasColumnType("jsonb").HasColumnName("bank_details");
    b.Property<string>("Code").IsRequired().HasColumnType("text").HasColumnName("code");
    b.Property<decimal?>("CommissionSplitSelf").ValueGeneratedOnAdd().HasColumnType("numeric").HasDefaultValue(50m).HasColumnName("commission_split_self");
    b.Property<decimal?>("CommissionSplitSub").ValueGeneratedOnAdd().HasColumnType("numeric").HasDefaultValue(50m).HasColumnName("commission_split_sub");
    b.Property<string>("Country").HasColumnType("text").HasColumnName("country");
    b.Property<string>("Currency").HasColumnType("text").HasColumnName("currency");
    b.Property<string>("CustomLogo").HasColumnType("text").HasColumnName("custom_logo");
    b.Property<JsonElement?>("CustomUrlSlugs").HasColumnType("jsonb").HasColumnName("custom_url_slugs");
    b.Property<string>("Description").HasColumnType("text").HasColumnName("description");
    b.Property<string>("DiscountType").IsRequired().HasColumnType("text").HasColumnName("discount_type");
    b.Property<decimal>("DiscountValue").HasColumnType("numeric").HasColumnName("discount_value");
    b.Property<string>("Email").HasColumnType("text").HasColumnName("email");
    b.Property<string>("FirstName").HasColumnType("text").HasColumnName("first_name");
    b.Property<string>("FooterLogoType").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("tourstore").HasColumnName("footer_logo_type");
    b.Property<string>("HeroApiKey").HasColumnType("text").HasColumnName("hero_api_key");
    b.Property<bool>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<string>("LandingHeroFont").HasColumnType("text").HasColumnName("landing_hero_font");
    b.Property<string>("LandingHeroImage").HasColumnType("text").HasColumnName("landing_hero_image");
    b.Property<string>("LandingHeroText").HasColumnType("text").HasColumnName("landing_hero_text");
    b.Property<string>("LandingHeroVideo").HasColumnType("text").HasColumnName("landing_hero_video");
    b.Property<string>("LandingPrimaryColor").HasColumnType("text").HasColumnName("landing_primary_color");
    b.Property<string>("LandingSecondaryColor").HasColumnType("text").HasColumnName("landing_secondary_color");
    b.Property<float?>("LandingVideoEndTime").HasColumnType("real").HasColumnName("landing_video_end_time");
    b.Property<string>("LandingVideoPlaybackSpeed").HasColumnType("text").HasColumnName("landing_video_playback_speed");
    b.Property<float?>("LandingVideoStartTime").HasColumnType("real").HasColumnName("landing_video_start_time");
    b.Property<string>("LastName").HasColumnType("text").HasColumnName("last_name");
    b.Property<string>("MasterAffiliateId").HasColumnType("text").HasColumnName("master_affiliate_id");
    b.Property<bool?>("NotifyPaymentDue").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("notify_payment_due");
    b.Property<bool?>("NotifyProductSold").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("notify_product_sold");
    b.Property<string>("ParentPromoCodeId").HasColumnType("text").HasColumnName("parent_promo_code_id");
    b.Property<string>("Password").HasColumnType("text").HasColumnName("password");
    b.Property<string>("Phone").HasColumnType("text").HasColumnName("phone");
    b.Property<string>("PlaceOfWork").HasColumnType("text").HasColumnName("place_of_work");
    b.Property<bool?>("PosEnabled").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("pos_enabled");
    b.Property<bool?>("ProcessingFeeAbsorbOverride").HasColumnType("boolean").HasColumnName("processing_fee_absorb_override");
    b.Property<string>("ProcessingFeeModeOverride").HasColumnType("text").HasColumnName("processing_fee_mode_override");
    b.Property<decimal?>("ProcessingFeePercentOverride").HasColumnType("numeric").HasColumnName("processing_fee_percent_override");
    b.Property<string>("ProfilePicture").HasColumnType("text").HasColumnName("profile_picture");
    b.Property<string>("RegionId").HasColumnType("text").HasColumnName("region_id");
    b.Property<decimal?>("SubAffiliateCommissionPercent").ValueGeneratedOnAdd().HasColumnType("numeric").HasDefaultValue(0m).HasColumnName("sub_affiliate_commission_percent");
    b.Property<JsonElement?>("TerritoryBoundaries").HasColumnType("jsonb").HasColumnName("territory_boundaries");
    b.Property<string>("TerritoryExclusivity").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("non-exclusive").HasColumnName("territory_exclusivity");
    b.Property<int>("UsageCount").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("usage_count");
    b.Property<int?>("UsageLimit").HasColumnType("integer").HasColumnName("usage_limit");
    b.Property<string>("ValidFrom").HasColumnType("text").HasColumnName("valid_from");
    b.Property<string>("ValidTo").HasColumnType("text").HasColumnName("valid_to");
    b.Property<string>("WebsiteDomain").HasColumnType("text").HasColumnName("website_domain");
    b.Property<string>("WidgetBackgroundColor").HasColumnType("text").HasColumnName("widget_background_color");
    b.Property<string>("WidgetFontFamily").HasColumnType("text").HasColumnName("widget_font_family");
    b.Property<string>("WidgetSubdomain").HasColumnType("text").HasColumnName("widget_subdomain");
    b.HasKey("Id");
    b.HasIndex("Code").IsUnique();
    b.ToTable("promo_codes");
});
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs:404" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs#L404">20260411054624_AddPerformanceIndexes.Designer.cs:404-2985</a> | AddPerformanceIndexes | BuildTargetModel |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs:404" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs#L404">AppDbContextModelSnapshot.cs:404-2985</a> | AppDbContextModelSnapshot | BuildModel |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 3: GRP-12-b39ddfdf

**Extractability:** extractable  
**Score:** 8.32  
**Occurrences:** 2  

**Preview:**

```csharp
modelBuilder.Entity("TourStore.Domain.Models.Section", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("LocationId").IsRequired().HasColumnType("text").HasColumnName("location_id");
    b.Property<int>("Order").HasColumnType("integer").HasColumnName("order");
    b.Property<JsonElement>("Rules").HasColumnType("jsonb").HasColumnName("rules");
    b.Property<string>("Title").IsRequired().HasColumnType("text").HasColumnName("title");
    b.HasKey("Id");
    b.ToTable("sections");
});
modelBuilder.Entity("TourStore.Domain.Models.Session", b =>
{
    b.Property<string>("Sid").HasColumnType("text").HasColumnName("sid");
    b.Property<DateTime>("Expire").HasColumnType("timestamp with time zone").HasColumnName("expire");
    b.Property<JsonElement>("Sess").HasColumnType("jsonb").HasColumnName("sess");
    b.HasKey("Sid");
    b.HasIndex("Expire").HasDatabaseName("IDX_session_expire");
    b.ToTable("sessions");
});
modelBuilder.Entity("TourStore.Domain.Models.Setting", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Key").IsRequired().HasColumnType("text").HasColumnName("key");
    b.Property<JsonElement>("Value").HasColumnType("jsonb").HasColumnName("value");
    b.HasKey("Id");
    b.HasIndex("Key").IsUnique();
    b.ToTable("settings");
});
modelBuilder.Entity("TourStore.Domain.Models.SharedItinerary", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<JsonElement?>("ActivityLog").HasColumnType("jsonb").HasColumnName("activity_log");
    b.Property<string>("AffiliateId").HasColumnType("text").HasColumnName("affiliate_id");
    b.Property<string>("AffiliatePromoCode").HasColumnType("text").HasColumnName("affiliate_promo_code");
    b.Property<decimal?>("AmountPaid").ValueGeneratedOnAdd().HasColumnType("numeric").HasDefaultValue(0m).HasColumnName("amount_paid");
    b.Property<bool>("Archived").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("archived");
    b.Property<JsonElement>("CartItems").HasColumnType("jsonb").HasColumnName("cart_items");
    b.Property<string>("CreatedAt").IsRequired().HasColumnType("text").HasColumnName("created_at");
    b.Property<string>("CustomerEmail").IsRequired().HasColumnType("text").HasColumnName("customer_email");
    b.Property<string>("CustomerFirstName").IsRequired().HasColumnType("text").HasColumnName("customer_first_name");
    b.Property<string>("CustomerLastName").IsRequired().HasColumnType("text").HasColumnName("customer_last_name");
    b.Property<string>("CustomerPhone").HasColumnType("text").HasColumnName("customer_phone");
    b.Property<string>("ExpiresAt").HasColumnType("text").HasColumnName("expires_at");
    b.Property<string>("HeroBookingId").HasColumnType("text").HasColumnName("hero_booking_id");
    b.Property<string>("HeroOrderNumber").HasColumnType("text").HasColumnName("hero_order_number");
    b.Property<string>("HeroPaxIds").HasColumnType("text").HasColumnName("hero_pax_ids");
    b.Property<string>("HeroQuoteNumber").HasColumnType("text").HasColumnName("hero_quote_number");
    b.Property<decimal?>("HeroSurcharge").HasColumnType("numeric").HasColumnName("hero_surcharge");
    b.Property<decimal?>("HeroTotal").HasColumnType("numeric").HasColumnName("hero_total");
    b.Property<string>("LocationId").HasColumnType("text").HasColumnName("location_id");
    b.Property<string>("PaidAt").HasColumnType("text").HasColumnName("paid_at");
    b.Property<DateTime?>("PiiScrubbedAt").HasColumnType("timestamp with time zone").HasColumnName("pii_scrubbed_at");
    b.Property<string>("PiiScrubbedBy").HasColumnType("text").HasColumnName("pii_scrubbed_by");
    b.Property<JsonElement?>("QuestionAnswers").HasColumnType("jsonb").HasColumnName("question_answers");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("pending").HasColumnName("status");
    b.Property<string>("StripeSessionId").HasColumnType("text").HasColumnName("stripe_session_id");
    b.Property<decimal?>("TipAmount").HasColumnType("numeric").HasColumnName("tip_amount");
    b.Property<decimal>("Total").HasColumnType("numeric").HasColumnName("total");
    b.Property<string>("TourStoreOrderNumber").HasColumnType("text").HasColumnName("tour_store_order_number");
    b.HasKey("Id");
    b.ToTable("shared_itineraries");
});
modelBuilder.Entity("TourStore.Domain.Models.Supplier", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("AccountsEmail").HasColumnType("text").HasColumnName("accounts_email");
    b.Property<bool?>("AccountsNotifyBookings").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("accounts_notify_bookings");
    b.Property<bool?>("AccountsNotifyFinance").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("accounts_notify_finance");
    b.Property<string>("AccountsPhone").HasColumnType("text").HasColumnName("accounts_phone");
    b.Property<string>("Address").HasColumnType("text").HasColumnName("address");
    b.Property<bool?>("AgreementAccepted").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("agreement_accepted");
    b.Property<JsonElement?>("Amenities").HasColumnType("jsonb").HasColumnName("amenities");
    b.Property<string>("BankAccountName").HasColumnType("text").HasColumnName("bank_account_name");
    b.Property<string>("BankDetails1").HasColumnType("text").HasColumnName("bank_details1");
    b.Property<string>("BankDetails2").HasColumnType("text").HasColumnName("bank_details2");
    b.Property<string>("BankDetails3").HasColumnType("text").HasColumnName("bank_details3");
    b.Property<JsonElement?>("CachedReviews").HasColumnType("jsonb").HasColumnName("cached_reviews");
    b.Property<decimal?>("CancellationFee1").HasColumnType("numeric").HasColumnName("cancellation_fee1");
    b.Property<decimal?>("CancellationFee2").HasColumnType("numeric").HasColumnName("cancellation_fee2");
    b.Property<decimal?>("CancellationFee3").HasColumnType("numeric").HasColumnName("cancellation_fee3");
    b.Property<string>("CancellationPeriod1").HasColumnType("text").HasColumnName("cancellation_period1");
    b.Property<string>("CancellationPeriod2").HasColumnType("text").HasColumnName("cancellation_period2");
    b.Property<string>("CancellationPeriod3").HasColumnType("text").HasColumnName("cancellation_period3");
    b.Property<bool?>("CardFeeAccepted").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("card_fee_accepted");
    b.Property<bool?>("CleanupPickups").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("cleanup_pickups");
    b.Property<string>("Country").HasColumnType("text").HasColumnName("country");
    b.Property<string>("Currency").HasColumnType("text").HasColumnName("currency");
    b.Property<string>("DefaultLocationId").HasColumnType("text").HasColumnName("default_location_id");
    b.Property<string>("Email").HasColumnType("text").HasColumnName("email");
    b.Property<JsonElement?>("ExcludedPickupNames").HasColumnType("jsonb").HasColumnName("excluded_pickup_names");
    b.Property<string>("FacebookUrl").HasColumnType("text").HasColumnName("facebook_url");
    b.Property<bool?>("FacebookVerified").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("facebook_verified");
    b.Property<string>("GooglePlaceId").HasColumnType("text").HasColumnName("google_place_id");
    b.Property<string>("GooglePlaceUrl").HasColumnType("text").HasColumnName("google_place_url");
    b.Property<bool?>("GoogleVerified").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("google_verified");
    b.Property<string>("HeroConnectionStatus").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Not Connected").HasColumnName("hero_connection_status");
    b.Property<string>("HeroSupplierId").HasColumnType("text").HasColumnName("hero_supplier_id");
    b.Property<string>("HeroSupplierName").HasColumnType("text").HasColumnName("hero_supplier_name");
    b.Property<string>("InstagramUrl").HasColumnType("text").HasColumnName("instagram_url");
    b.Property<bool?>("InstagramVerified").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("instagram_verified");
    b.Property<string>("JobTitle").HasColumnType("text").HasColumnName("job_title");
    b.Property<string>("Logo").HasColumnType("text").HasColumnName("logo");
    b.Property<string>("ManagedByRegionalPartnerId").HasColumnType("text").HasColumnName("managed_by_regional_partner_id");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("Phone").HasColumnType("text").HasColumnName("phone");
    b.Property<string>("PickupOutputFormat").HasColumnType("text").HasColumnName("pickup_output_format");
    b.Property<JsonElement?>("PickupRenameRules").HasColumnType("jsonb").HasColumnName("pickup_rename_rules");
    b.Property<JsonElement?>("PickupStripPrefixes").HasColumnType("jsonb").HasColumnName("pickup_strip_prefixes");
    b.Property<string>("ReservationSystem").HasColumnType("text").HasColumnName("reservation_system");
    b.Property<string>("ReservationsEmail").HasColumnType("text").HasColumnName("reservations_email");
    b.Property<bool?>("ReservationsNotifyBookings").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("reservations_notify_bookings");
    b.Property<bool?>("ReservationsNotifyFinance").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("reservations_notify_finance");
    b.Property<string>("ReservationsPhone").HasColumnType("text").HasColumnName("reservations_phone");
    b.Property<JsonElement?>("ReviewSettings").HasColumnType("jsonb").HasColumnName("review_settings");
    b.Property<DateTime?>("ReviewsLastFetched").HasColumnType("timestamp with time zone").HasColumnName("reviews_last_fetched");
    b.Property<DateTime?>("SignedAt").HasColumnType("timestamp with time zone").HasColumnName("signed_at");
    b.Property<string>("SignedBy").HasColumnType("text").HasColumnName("signed_by");
    b.Property<string>("Slug").HasColumnType("text").HasColumnName("slug");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Active").HasColumnName("status");
    b.Property<decimal?>("SupplierCommissionRate").HasColumnType("numeric").HasColumnName("supplier_commission_rate");
    b.Property<string>("SupplierTermsUrl").HasColumnType("text").HasColumnName("supplier_terms_url");
    b.Property<string>("TripAdvisorUrl").HasColumnType("text").HasColumnName("trip_advisor_url");
    b.Property<bool?>("TripAdvisorVerified").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("trip_advisor_verified");
    b.Property<string>("Website").HasColumnType("text").HasColumnName("website");
    b.HasKey("Id");
    b.ToTable("suppliers");
});
modelBuilder.Entity("TourStore.Domain.Models.SupplierAgreement", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Address").IsRequired().HasColumnType("text").HasColumnName("address");
    b.Property<string>("BrandName").HasColumnType("text").HasColumnName("brand_name");
    b.Property<decimal>("CommissionRate").HasColumnType("numeric").HasColumnName("commission_rate");
    b.Property<string>("CompanyName").IsRequired().HasColumnType("text").HasColumnName("company_name");
    b.Property<string>("CompanyWebsite").HasColumnType("text").HasColumnName("company_website");
    b.Property<DateTime?>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
    b.Property<decimal>("CreditCardFee").HasColumnType("numeric").HasColumnName("credit_card_fee");
    b.Property<string>("FullName").IsRequired().HasColumnType("text").HasColumnName("full_name");
    b.Property<string>("JobTitle").IsRequired().HasColumnType("text").HasColumnName("job_title");
    b.Property<string>("Logo").HasColumnType("text").HasColumnName("logo");
    b.Property<decimal>("NetReceive").HasColumnType("numeric").HasColumnName("net_receive");
    b.Property<string>("Status").IsRequired().HasColumnType("text").HasColumnName("status");
    b.Property<decimal>("TotalSpend").HasColumnType("numeric").HasColumnName("total_spend");
    b.HasKey("Id");
    b.ToTable("supplier_agreements");
});
modelBuilder.Entity("TourStore.Domain.Models.SupplierBranch", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Address").HasColumnType("text").HasColumnName("address");
    b.Property<JsonElement?>("Amenities").HasColumnType("jsonb").HasColumnName("amenities");
    b.Property<string>("BranchName").IsRequired().HasColumnType("text").HasColumnName("branch_name");
    b.Property<JsonElement?>("CachedReviews").HasColumnType("jsonb").HasColumnName("cached_reviews");
    b.Property<JsonElement?>("ContentUrls").HasColumnType("jsonb").HasColumnName("content_urls");
    b.Property<string>("Description").HasColumnType("text").HasColumnName("description");
    b.Property<int?>("DormAgeMax").HasColumnType("integer").HasColumnName("dorm_age_max");
    b.Property<int?>("DormAgeMin").HasColumnType("integer").HasColumnName("dorm_age_min");
    b.Property<string>("Email").HasColumnType("text").HasColumnName("email");
    b.Property<JsonElement?>("GalleryImages").HasColumnType("jsonb").HasColumnName("gallery_images");
    b.Property<string>("GooglePlaceId").HasColumnType("text").HasColumnName("google_place_id");
    b.Property<float?>("GooglePlaceLat").HasColumnType("real").HasColumnName("google_place_lat");
    b.Property<float?>("GooglePlaceLng").HasColumnType("real").HasColumnName("google_place_lng");
    b.Property<string>("GooglePlaceUrl").HasColumnType("text").HasColumnName("google_place_url");
    b.Property<string>("HeroBranchName").HasColumnType("text").HasColumnName("hero_branch_name");
    b.Property<string>("Highlight").HasColumnType("text").HasColumnName("highlight");
    b.Property<string>("LocationId").HasColumnType("text").HasColumnName("location_id");
    b.Property<string>("Phone").HasColumnType("text").HasColumnName("phone");
    b.Property<string>("Photo").HasColumnType("text").HasColumnName("photo");
    b.Property<int?>("PrivateAgeMax").HasColumnType("integer").HasColumnName("private_age_max");
    b.Property<int?>("PrivateAgeMin").HasColumnType("integer").HasColumnName("private_age_min");
    b.Property<JsonElement?>("PropertySummary").HasColumnType("jsonb").HasColumnName("property_summary");
    b.Property<DateTime?>("ReviewsLastFetched").HasColumnType("timestamp with time zone").HasColumnName("reviews_last_fetched");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Active").HasColumnName("status");
    b.Property<string>("SupplierId").IsRequired().HasColumnType("text").HasColumnName("supplier_id");
    b.Property<float?>("VideoEndTime").HasColumnType("real").HasColumnName("video_end_time");
    b.Property<float?>("VideoPlaybackRate").ValueGeneratedOnAdd().HasColumnType("real").HasDefaultValue(1f).HasColumnName("video_playback_rate");
    b.Property<float?>("VideoStartTime").ValueGeneratedOnAdd().HasColumnType("real").HasDefaultValue(0f).HasColumnName("video_start_time");
    b.Property<string>("YoutubeVideo").HasColumnType("text").HasColumnName("youtube_video");
    b.HasKey("Id");
    b.ToTable("supplier_branches");
});
modelBuilder.Entity("TourStore.Domain.Models.SupplierProductApproval", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("ApprovedById").IsRequired().HasColumnType("text").HasColumnName("approved_by_id");
    b.Property<string>("ApprovedByName").IsRequired().HasColumnType("text").HasColumnName("approved_by_name");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Notes").HasColumnType("text").HasColumnName("notes");
    b.Property<string>("ProductId").IsRequired().HasColumnType("text").HasColumnName("product_id");
    b.Property<JsonElement>("ProductSnapshot").HasColumnType("jsonb").HasColumnName("product_snapshot");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("pending").HasColumnName("status");
    b.Property<string>("SupplierId").IsRequired().HasColumnType("text").HasColumnName("supplier_id");
    b.HasKey("Id");
    b.ToTable("supplier_product_approvals");
});
modelBuilder.Entity("TourStore.Domain.Models.SupplierProductComment", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Comment").IsRequired().HasColumnType("text").HasColumnName("comment");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<bool?>("IsInternal").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("is_internal");
    b.Property<string>("ProductId").IsRequired().HasColumnType("text").HasColumnName("product_id");
    b.Property<string>("SupplierId").IsRequired().HasColumnType("text").HasColumnName("supplier_id");
    b.Property<string>("UserName").IsRequired().HasColumnType("text").HasColumnName("user_name");
    b.Property<string>("UserRole").IsRequired().HasColumnType("text").HasColumnName("user_role");
    b.HasKey("Id");
    b.ToTable("supplier_product_comments");
});
modelBuilder.Entity("TourStore.Domain.Models.SupplierTeamMember", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Email").IsRequired().HasColumnType("text").HasColumnName("email");
    b.Property<bool?>("InviteAccepted").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("invite_accepted");
    b.Property<string>("InviteToken").HasColumnType("text").HasColumnName("invite_token");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("Role").HasColumnType("text").HasColumnName("role");
    b.Property<string>("SupplierId").IsRequired().HasColumnType("text").HasColumnName("supplier_id");
    b.HasKey("Id");
    b.ToTable("supplier_team_members");
});
modelBuilder.Entity("TourStore.Domain.Models.UrlRedirect", b =>
{
    b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("id");
    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
    b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
    b.Property<string>("FromPath").IsRequired().HasColumnType("text").HasColumnName("from_path");
    b.Property<bool>("IsActive").HasColumnType("boolean").HasColumnName("is_active");
    b.Property<string>("Note").HasColumnType("text").HasColumnName("note");
    b.Property<string>("RedirectType").IsRequired().HasColumnType("text").HasColumnName("redirect_type");
    b.Property<string>("ToUrl").IsRequired().HasColumnType("text").HasColumnName("to_url");
    b.HasKey("Id");
    b.ToTable("url_redirects");
});
modelBuilder.Entity("TourStore.Domain.Models.User", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Avatar").HasColumnType("text").HasColumnName("avatar");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Email").HasColumnType("text").HasColumnName("email");
    b.Property<string>("FirstName").HasColumnType("text").HasColumnName("first_name");
    b.Property<bool>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<string>("LastName").HasColumnType("text").HasColumnName("last_name");
    b.Property<string>("Name").HasColumnType("text").HasColumnName("name");
    b.Property<string>("Password").HasColumnType("text").HasColumnName("password");
    b.Property<string>("ProfileImageUrl").HasColumnType("text").HasColumnName("profile_image_url");
    b.Property<string>("PromoCode").HasColumnType("text").HasColumnName("promo_code");
    b.Property<string>("RecoveryCodes").HasColumnType("text").HasColumnName("recovery_codes");
    b.Property<string>("Role").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("user").HasColumnName("role");
    b.Property<string>("SsoId").HasColumnType("text").HasColumnName("sso_id");
    b.Property<string>("SsoProvider").HasColumnType("text").HasColumnName("sso_provider");
    b.Property<bool>("TotpEnabled").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("totp_enabled");
    b.Property<string>("TotpSecret").HasColumnType("text").HasColumnName("totp_secret");
    b.Property<DateTime?>("UpdatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("updated_at").HasDefaultValueSql("now()");
    b.HasKey("Id");
    b.HasIndex("Email").IsUnique();
    b.ToTable("users");
});
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs:3120" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs#L3120">20260411054624_AddPerformanceIndexes.Designer.cs:3120-4105</a> | AddPerformanceIndexes | BuildTargetModel |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs:3128" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs#L3128">AppDbContextModelSnapshot.cs:3128-4113</a> | AppDbContextModelSnapshot | BuildModel |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 4: GRP-10-1f2822c5

**Extractability:** extractable  
**Score:** 6.93  
**Occurrences:** 2  

**Preview:**

```csharp
modelBuilder.Entity("TourStore.Domain.Models.AdminNote", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Category").IsRequired().HasColumnType("text").HasColumnName("category");
    b.Property<string>("Content").IsRequired().HasColumnType("text").HasColumnName("content");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("CreatedBy").HasColumnType("text").HasColumnName("created_by");
    b.Property<bool>("IsPinned").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("is_pinned");
    b.Property<string>("Title").IsRequired().HasColumnType("text").HasColumnName("title");
    b.Property<DateTime?>("UpdatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("updated_at").HasDefaultValueSql("now()");
    b.HasKey("Id");
    b.ToTable("admin_notes");
});
modelBuilder.Entity("TourStore.Domain.Models.AffiliateActivityLog", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Action").IsRequired().HasColumnType("text").HasColumnName("action");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<JsonElement?>("Details").HasColumnType("jsonb").HasColumnName("details");
    b.Property<string>("IpAddress").HasColumnType("text").HasColumnName("ip_address");
    b.Property<string>("PromoCode").IsRequired().HasColumnType("text").HasColumnName("promo_code");
    b.Property<string>("UserAgent").HasColumnType("text").HasColumnName("user_agent");
    b.HasKey("Id");
    b.ToTable("affiliate_activity_logs");
});
modelBuilder.Entity("TourStore.Domain.Models.AffiliateApplication", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("AboutYourself").HasColumnType("text").HasColumnName("about_yourself");
    b.Property<string>("AffiliateEntityType").HasColumnType("text").HasColumnName("affiliate_entity_type");
    b.Property<string>("AustralianBusinessNumber").HasColumnType("text").HasColumnName("australian_business_number");
    b.Property<string>("BankAccountName").IsRequired().HasColumnType("text").HasColumnName("bank_account_name");
    b.Property<string>("BankDetails1").IsRequired().HasColumnType("text").HasColumnName("bank_details_1");
    b.Property<string>("BankDetails2").IsRequired().HasColumnType("text").HasColumnName("bank_details_2");
    b.Property<string>("BankDetails3").HasColumnType("text").HasColumnName("bank_details_3");
    b.Property<string>("BusinessType").IsRequired().HasColumnType("text").HasColumnName("business_type");
    b.Property<string>("CompanyName").HasColumnType("text").HasColumnName("company_name");
    b.Property<string>("CompanyWebsite").HasColumnType("text").HasColumnName("company_website");
    b.Property<string>("Country").IsRequired().HasColumnType("text").HasColumnName("country");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Email").IsRequired().HasColumnType("text").HasColumnName("email");
    b.Property<string>("FirstName").IsRequired().HasColumnType("text").HasColumnName("first_name");
    b.Property<string>("ImageUrl").HasColumnType("text").HasColumnName("image_url");
    b.Property<string>("LastName").IsRequired().HasColumnType("text").HasColumnName("last_name");
    b.Property<string>("Notes").HasColumnType("text").HasColumnName("notes");
    b.Property<string>("Phone").HasColumnType("text").HasColumnName("phone");
    b.Property<DateTime?>("ReviewedAt").HasColumnType("timestamp with time zone").HasColumnName("reviewed_at");
    b.Property<string>("ReviewedBy").HasColumnType("text").HasColumnName("reviewed_by");
    b.Property<List<string>>("SelectedCities").IsRequired().HasColumnType("text[]").HasColumnName("selected_cities");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("pending").HasColumnName("status");
    b.Property<string>("W9Address").HasColumnType("text").HasColumnName("w9_address");
    b.Property<string>("W9BusinessName").HasColumnType("text").HasColumnName("w9_business_name");
    b.Property<bool?>("W9CertificationAgreed").HasColumnType("boolean").HasColumnName("w9_certification_agreed");
    b.Property<string>("W9City").HasColumnType("text").HasColumnName("w9_city");
    b.Property<string>("W9ExemptFatcaCode").HasColumnType("text").HasColumnName("w9_exempt_fatca_code");
    b.Property<string>("W9ExemptPayeeCode").HasColumnType("text").HasColumnName("w9_exempt_payee_code");
    b.Property<string>("W9LegalName").HasColumnType("text").HasColumnName("w9_legal_name");
    b.Property<string>("W9Signature").HasColumnType("text").HasColumnName("w9_signature");
    b.Property<DateTime?>("W9SignedAt").HasColumnType("timestamp with time zone").HasColumnName("w9_signed_at");
    b.Property<string>("W9State").HasColumnType("text").HasColumnName("w9_state");
    b.Property<string>("W9TaxClassification").HasColumnType("text").HasColumnName("w9_tax_classification");
    b.Property<string>("W9Tin").HasColumnType("text").HasColumnName("w9_tin");
    b.Property<string>("W9TinType").HasColumnType("text").HasColumnName("w9_tin_type");
    b.Property<string>("W9ZipCode").HasColumnType("text").HasColumnName("w9_zip_code");
    b.HasKey("Id");
    b.ToTable("affiliate_applications");
});
modelBuilder.Entity("TourStore.Domain.Models.ApiCallLog", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Context").HasColumnType("text").HasColumnName("context");
    b.Property<DateTime>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Endpoint").IsRequired().HasColumnType("text").HasColumnName("endpoint");
    b.Property<string>("ErrorMessage").HasColumnType("text").HasColumnName("error_message");
    b.Property<string>("Method").IsRequired().HasColumnType("text").HasColumnName("method");
    b.Property<string>("RequestBody").HasColumnType("text").HasColumnName("request_body");
    b.Property<string>("RequestHeaders").HasColumnType("text").HasColumnName("request_headers");
    b.Property<string>("ResponseBody").HasColumnType("text").HasColumnName("response_body");
    b.Property<int?>("ResponseTime").HasColumnType("integer").HasColumnName("response_time");
    b.Property<int>("StatusCode").HasColumnType("integer").HasColumnName("status_code");
    b.Property<bool>("Success").HasColumnType("boolean").HasColumnName("success");
    b.HasKey("Id");
    b.ToTable("api_call_logs");
});
modelBuilder.Entity("TourStore.Domain.Models.AuditLog", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Action").IsRequired().HasColumnType("text").HasColumnName("action");
    b.Property<JsonElement?>("Changes").HasColumnType("jsonb").HasColumnName("changes");
    b.Property<DateTime>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("EntityId").HasColumnType("text").HasColumnName("entity_id");
    b.Property<string>("EntityName").HasColumnType("text").HasColumnName("entity_name");
    b.Property<string>("EntityType").IsRequired().HasColumnType("text").HasColumnName("entity_type");
    b.Property<string>("IpAddress").HasColumnType("text").HasColumnName("ip_address");
    b.Property<string>("UserAgent").HasColumnType("text").HasColumnName("user_agent");
    b.Property<string>("UserEmail").HasColumnType("text").HasColumnName("user_email");
    b.Property<string>("UserId").HasColumnType("text").HasColumnName("user_id");
    b.HasKey("Id");
    b.ToTable("audit_logs");
});
modelBuilder.Entity("TourStore.Domain.Models.AuthSession", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<DateTime>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<DateTime>("ExpiresAt").HasColumnType("timestamp with time zone").HasColumnName("expires_at");
    b.Property<string>("IpAddress").HasColumnType("text").HasColumnName("ip_address");
    b.Property<bool>("Revoked").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("revoked");
    b.Property<string>("SessionToken").IsRequired().HasColumnType("text").HasColumnName("session_token");
    b.Property<string>("UserAgent").HasColumnType("text").HasColumnName("user_agent");
    b.Property<string>("UserId").IsRequired().HasColumnType("text").HasColumnName("user_id");
    b.HasKey("Id");
    b.HasIndex("SessionToken").IsUnique();
    b.HasIndex("UserId");
    b.ToTable("auth_sessions");
});
modelBuilder.Entity("TourStore.Domain.Models.Badge", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Color").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("bg-emerald-600").HasColumnName("color");
    b.Property<string>("Description").HasColumnType("text").HasColumnName("description");
    b.Property<bool?>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<int?>("SortOrder").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("sort_order");
    b.HasKey("Id");
    b.ToTable("badges");
});
modelBuilder.Entity("TourStore.Domain.Models.BlogImageSource", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("BlogPostId").HasColumnType("text").HasColumnName("blog_post_id");
    b.Property<string>("BlogPostTitle").HasColumnType("text").HasColumnName("blog_post_title");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("ImageUrl").IsRequired().HasColumnType("text").HasColumnName("image_url");
    b.Property<string>("SearchQuery").HasColumnType("text").HasColumnName("search_query");
    b.Property<bool>("Selected").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("selected");
    b.Property<string>("SourcePage").HasColumnType("text").HasColumnName("source_page");
    b.Property<string>("SourceTitle").HasColumnType("text").HasColumnName("source_title");
    b.Property<string>("ThumbnailUrl").HasColumnType("text").HasColumnName("thumbnail_url");
    b.HasKey("Id");
    b.ToTable("blog_image_sources");
});
modelBuilder.Entity("TourStore.Domain.Models.BlogPost", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Content").IsRequired().HasColumnType("text").HasColumnName("content");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<List<string>>("FeaturedProductIds").HasColumnType("text[]").HasColumnName("featured_product_ids");
    b.Property<List<string>>("FeaturedSupplierIds").HasColumnType("text[]").HasColumnName("featured_supplier_ids");
    b.Property<string>("Image").HasColumnType("text").HasColumnName("image");
    b.Property<List<string>>("Keywords").HasColumnType("text[]").HasColumnName("keywords");
    b.Property<string>("LocationId").HasColumnType("text").HasColumnName("location_id");
    b.Property<List<string>>("ReferenceUrls").HasColumnType("text[]").HasColumnName("reference_urls");
    b.Property<string>("Slug").IsRequired().HasColumnType("text").HasColumnName("slug");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("draft").HasColumnName("status");
    b.Property<string>("Summary").IsRequired().HasColumnType("text").HasColumnName("summary");
    b.Property<int?>("TargetLength").HasColumnType("integer").HasColumnName("target_length");
    b.Property<string>("ThemeId").HasColumnType("text").HasColumnName("theme_id");
    b.Property<string>("Title").IsRequired().HasColumnType("text").HasColumnName("title");
    b.Property<DateTime?>("UpdatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("updated_at").HasDefaultValueSql("now()");
    b.HasKey("Id");
    b.ToTable("blog_posts");
});
modelBuilder.Entity("TourStore.Domain.Models.BlogTheme", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<bool?>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<List<string>>("Keywords").HasColumnType("text[]").HasColumnName("keywords");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("ProductFilter").HasColumnType("text").HasColumnName("product_filter");
    b.Property<List<string>>("ReferenceUrls").HasColumnType("text[]").HasColumnName("reference_urls");
    b.Property<string>("SummaryTemplate").IsRequired().HasColumnType("text").HasColumnName("summary_template");
    b.Property<int?>("TargetLength").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(800).HasColumnName("target_length");
    b.Property<string>("TitleTemplate").IsRequired().HasColumnType("text").HasColumnName("title_template");
    b.HasKey("Id");
    b.ToTable("blog_themes");
});
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.Designer.cs:79" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.Designer.cs#L79">20260402021243_InitialCreate.Designer.cs:79-745</a> | InitialCreate | BuildTargetModel |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs:81" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs#L81">20260411054624_AddPerformanceIndexes.Designer.cs:81-747</a> | AddPerformanceIndexes | BuildTargetModel |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 5: GRP-6-04702e7d

**Extractability:** extractable  
**Score:** 6.59  
**Occurrences:** 3  

**Preview:**

```csharp
modelBuilder.Entity("TourStore.Domain.Models.SupplierBranch", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Address").HasColumnType("text").HasColumnName("address");
    b.Property<JsonElement?>("Amenities").HasColumnType("jsonb").HasColumnName("amenities");
    b.Property<string>("BranchName").IsRequired().HasColumnType("text").HasColumnName("branch_name");
    b.Property<JsonElement?>("CachedReviews").HasColumnType("jsonb").HasColumnName("cached_reviews");
    b.Property<JsonElement?>("ContentUrls").HasColumnType("jsonb").HasColumnName("content_urls");
    b.Property<string>("Description").HasColumnType("text").HasColumnName("description");
    b.Property<int?>("DormAgeMax").HasColumnType("integer").HasColumnName("dorm_age_max");
    b.Property<int?>("DormAgeMin").HasColumnType("integer").HasColumnName("dorm_age_min");
    b.Property<string>("Email").HasColumnType("text").HasColumnName("email");
    b.Property<JsonElement?>("GalleryImages").HasColumnType("jsonb").HasColumnName("gallery_images");
    b.Property<string>("GooglePlaceId").HasColumnType("text").HasColumnName("google_place_id");
    b.Property<float?>("GooglePlaceLat").HasColumnType("real").HasColumnName("google_place_lat");
    b.Property<float?>("GooglePlaceLng").HasColumnType("real").HasColumnName("google_place_lng");
    b.Property<string>("GooglePlaceUrl").HasColumnType("text").HasColumnName("google_place_url");
    b.Property<string>("HeroBranchName").HasColumnType("text").HasColumnName("hero_branch_name");
    b.Property<string>("Highlight").HasColumnType("text").HasColumnName("highlight");
    b.Property<string>("LocationId").HasColumnType("text").HasColumnName("location_id");
    b.Property<string>("Phone").HasColumnType("text").HasColumnName("phone");
    b.Property<string>("Photo").HasColumnType("text").HasColumnName("photo");
    b.Property<int?>("PrivateAgeMax").HasColumnType("integer").HasColumnName("private_age_max");
    b.Property<int?>("PrivateAgeMin").HasColumnType("integer").HasColumnName("private_age_min");
    b.Property<JsonElement?>("PropertySummary").HasColumnType("jsonb").HasColumnName("property_summary");
    b.Property<DateTime?>("ReviewsLastFetched").HasColumnType("timestamp with time zone").HasColumnName("reviews_last_fetched");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Active").HasColumnName("status");
    b.Property<string>("SupplierId").IsRequired().HasColumnType("text").HasColumnName("supplier_id");
    b.Property<float?>("VideoEndTime").HasColumnType("real").HasColumnName("video_end_time");
    b.Property<float?>("VideoPlaybackRate").ValueGeneratedOnAdd().HasColumnType("real").HasDefaultValue(1f).HasColumnName("video_playback_rate");
    b.Property<float?>("VideoStartTime").ValueGeneratedOnAdd().HasColumnType("real").HasDefaultValue(0f).HasColumnName("video_start_time");
    b.Property<string>("YoutubeVideo").HasColumnType("text").HasColumnName("youtube_video");
    b.HasKey("Id");
    b.ToTable("supplier_branches");
});
modelBuilder.Entity("TourStore.Domain.Models.SupplierProductApproval", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("ApprovedById").IsRequired().HasColumnType("text").HasColumnName("approved_by_id");
    b.Property<string>("ApprovedByName").IsRequired().HasColumnType("text").HasColumnName("approved_by_name");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Notes").HasColumnType("text").HasColumnName("notes");
    b.Property<string>("ProductId").IsRequired().HasColumnType("text").HasColumnName("product_id");
    b.Property<JsonElement>("ProductSnapshot").HasColumnType("jsonb").HasColumnName("product_snapshot");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("pending").HasColumnName("status");
    b.Property<string>("SupplierId").IsRequired().HasColumnType("text").HasColumnName("supplier_id");
    b.HasKey("Id");
    b.ToTable("supplier_product_approvals");
});
modelBuilder.Entity("TourStore.Domain.Models.SupplierProductComment", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Comment").IsRequired().HasColumnType("text").HasColumnName("comment");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<bool?>("IsInternal").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("is_internal");
    b.Property<string>("ProductId").IsRequired().HasColumnType("text").HasColumnName("product_id");
    b.Property<string>("SupplierId").IsRequired().HasColumnType("text").HasColumnName("supplier_id");
    b.Property<string>("UserName").IsRequired().HasColumnType("text").HasColumnName("user_name");
    b.Property<string>("UserRole").IsRequired().HasColumnType("text").HasColumnName("user_role");
    b.HasKey("Id");
    b.ToTable("supplier_product_comments");
});
modelBuilder.Entity("TourStore.Domain.Models.SupplierTeamMember", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Email").IsRequired().HasColumnType("text").HasColumnName("email");
    b.Property<bool?>("InviteAccepted").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("invite_accepted");
    b.Property<string>("InviteToken").HasColumnType("text").HasColumnName("invite_token");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("Role").HasColumnType("text").HasColumnName("role");
    b.Property<string>("SupplierId").IsRequired().HasColumnType("text").HasColumnName("supplier_id");
    b.HasKey("Id");
    b.ToTable("supplier_team_members");
});
modelBuilder.Entity("TourStore.Domain.Models.UrlRedirect", b =>
{
    b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer").HasColumnName("id");
    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<int>("Id"));
    b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone").HasColumnName("created_at");
    b.Property<string>("FromPath").IsRequired().HasColumnType("text").HasColumnName("from_path");
    b.Property<bool>("IsActive").HasColumnType("boolean").HasColumnName("is_active");
    b.Property<string>("Note").HasColumnType("text").HasColumnName("note");
    b.Property<string>("RedirectType").IsRequired().HasColumnType("text").HasColumnName("redirect_type");
    b.Property<string>("ToUrl").IsRequired().HasColumnType("text").HasColumnName("to_url");
    b.HasKey("Id");
    b.ToTable("url_redirects");
});
modelBuilder.Entity("TourStore.Domain.Models.User", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Avatar").HasColumnType("text").HasColumnName("avatar");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Email").HasColumnType("text").HasColumnName("email");
    b.Property<string>("FirstName").HasColumnType("text").HasColumnName("first_name");
    b.Property<bool>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<string>("LastName").HasColumnType("text").HasColumnName("last_name");
    b.Property<string>("Name").HasColumnType("text").HasColumnName("name");
    b.Property<string>("Password").HasColumnType("text").HasColumnName("password");
    b.Property<string>("ProfileImageUrl").HasColumnType("text").HasColumnName("profile_image_url");
    b.Property<string>("PromoCode").HasColumnType("text").HasColumnName("promo_code");
    b.Property<string>("RecoveryCodes").HasColumnType("text").HasColumnName("recovery_codes");
    b.Property<string>("Role").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("user").HasColumnName("role");
    b.Property<string>("SsoId").HasColumnType("text").HasColumnName("sso_id");
    b.Property<string>("SsoProvider").HasColumnType("text").HasColumnName("sso_provider");
    b.Property<bool>("TotpEnabled").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("totp_enabled");
    b.Property<string>("TotpSecret").HasColumnType("text").HasColumnName("totp_secret");
    b.Property<DateTime?>("UpdatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("updated_at").HasDefaultValueSql("now()");
    b.HasKey("Id");
    b.HasIndex("Email").IsUnique();
    b.ToTable("users");
});
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.Designer.cs:3673" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.Designer.cs#L3673">20260402021243_InitialCreate.Designer.cs:3673-4097</a> | InitialCreate | BuildTargetModel |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs:3681" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs#L3681">20260411054624_AddPerformanceIndexes.Designer.cs:3681-4105</a> | AddPerformanceIndexes | BuildTargetModel |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs:3689" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs#L3689">AppDbContextModelSnapshot.cs:3689-4113</a> | AppDbContextModelSnapshot | BuildModel |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 6: GRP-4-e4eb367f

**Extractability:** extractable  
**Score:** 4.39  
**Occurrences:** 3  

**Preview:**

```csharp
modelBuilder.Entity("TourStore.Domain.Models.ComboProduct", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("ComboId").IsRequired().HasColumnType("text").HasColumnName("combo_id");
    b.Property<string>("ProductId").IsRequired().HasColumnType("text").HasColumnName("product_id");
    b.Property<int?>("SortOrder").ValueGeneratedOnAdd().HasColumnType("integer").HasDefaultValue(0).HasColumnName("sort_order");
    b.HasKey("Id");
    b.ToTable("combo_products");
});
modelBuilder.Entity("TourStore.Domain.Models.ContactSubmission", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Email").IsRequired().HasColumnType("text").HasColumnName("email");
    b.Property<string>("Message").IsRequired().HasColumnType("text").HasColumnName("message");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("Notes").HasColumnType("text").HasColumnName("notes");
    b.Property<string>("Phone").HasColumnType("text").HasColumnName("phone");
    b.Property<string>("ProductName").HasColumnType("text").HasColumnName("product_name");
    b.Property<DateTime?>("RespondedAt").HasColumnType("timestamp with time zone").HasColumnName("responded_at");
    b.Property<string>("RespondedBy").HasColumnType("text").HasColumnName("responded_by");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("new").HasColumnName("status");
    b.Property<string>("SupplierName").HasColumnType("text").HasColumnName("supplier_name");
    b.Property<string>("Type").IsRequired().HasColumnType("text").HasColumnName("type");
    b.HasKey("Id");
    b.ToTable("contact_submissions");
});
modelBuilder.Entity("TourStore.Domain.Models.EmailLog", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("ErrorMessage").HasColumnType("text").HasColumnName("error_message");
    b.Property<string>("ItineraryId").HasColumnType("text").HasColumnName("itinerary_id");
    b.Property<JsonElement?>("Metadata").HasColumnType("jsonb").HasColumnName("metadata");
    b.Property<string>("RecipientEmail").IsRequired().HasColumnType("text").HasColumnName("recipient_email");
    b.Property<string>("RecipientName").HasColumnType("text").HasColumnName("recipient_name");
    b.Property<string>("ResendId").HasColumnType("text").HasColumnName("resend_id");
    b.Property<DateTime?>("SentAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("sent_at").HasDefaultValueSql("now()");
    b.Property<string>("SentBy").HasColumnType("text").HasColumnName("sent_by");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("sent").HasColumnName("status");
    b.Property<string>("Subject").IsRequired().HasColumnType("text").HasColumnName("subject");
    b.Property<string>("TemplateName").HasColumnType("text").HasColumnName("template_name");
    b.Property<string>("TemplateSlug").HasColumnType("text").HasColumnName("template_slug");
    b.HasKey("Id");
    b.ToTable("email_logs");
});
modelBuilder.Entity("TourStore.Domain.Models.EmailTemplate", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Description").HasColumnType("text").HasColumnName("description");
    b.Property<string>("HtmlBody").IsRequired().HasColumnType("text").HasColumnName("html_body");
    b.Property<bool>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("Slug").IsRequired().HasColumnType("text").HasColumnName("slug");
    b.Property<string>("Subject").IsRequired().HasColumnType("text").HasColumnName("subject");
    b.Property<DateTime?>("UpdatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("updated_at").HasDefaultValueSql("now()");
    b.Property<JsonElement?>("Variables").HasColumnType("jsonb").HasColumnName("variables");
    b.HasKey("Id");
    b.HasIndex("Slug").IsUnique();
    b.ToTable("email_templates");
});
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.Designer.cs:1195" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.Designer.cs#L1195">20260402021243_InitialCreate.Designer.cs:1195-1422</a> | InitialCreate | BuildTargetModel |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs:1197" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs#L1197">20260411054624_AddPerformanceIndexes.Designer.cs:1197-1424</a> | AddPerformanceIndexes | BuildTargetModel |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs:1197" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs#L1197">AppDbContextModelSnapshot.cs:1197-1424</a> | AppDbContextModelSnapshot | BuildModel |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 7: GRP-3-06c4103d

**Extractability:** extractable  
**Score:** 4.16  
**Occurrences:** 4  

**Preview:**

```csharp
migrationBuilder.AlterColumn<decimal>(name: "supplier_commission_rate", table: "suppliers", type: "numeric", nullable: true, oldClrType: typeof(float), oldType: "real", oldNullable: true);
migrationBuilder.AlterColumn<decimal>(name: "cancellation_fee3", table: "suppliers", type: "numeric", nullable: true, oldClrType: typeof(float), oldType: "real", oldNullable: true);
migrationBuilder.AlterColumn<decimal>(name: "cancellation_fee2", table: "suppliers", type: "numeric", nullable: true, oldClrType: typeof(float), oldType: "real", oldNullable: true);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:13" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L13">20260411054624_AddPerformanceIndexes.cs:13-38</a> | AddPerformanceIndexes | Up |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:22" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L22">20260411054624_AddPerformanceIndexes.cs:22-47</a> | AddPerformanceIndexes | Up |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:89" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L89">20260411054624_AddPerformanceIndexes.cs:89-114</a> | AddPerformanceIndexes | Up |
| 4 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:237" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L237">20260411054624_AddPerformanceIndexes.cs:237-262</a> | AddPerformanceIndexes | Up |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 8: GRP-3-f17696e5

**Extractability:** extractable  
**Score:** 4.16  
**Occurrences:** 4  

**Preview:**

```csharp
migrationBuilder.AlterColumn<float>(name: "supplier_commission_rate", table: "suppliers", type: "real", nullable: true, oldClrType: typeof(decimal), oldType: "numeric", oldNullable: true);
migrationBuilder.AlterColumn<float>(name: "cancellation_fee3", table: "suppliers", type: "real", nullable: true, oldClrType: typeof(decimal), oldType: "numeric", oldNullable: true);
migrationBuilder.AlterColumn<float>(name: "cancellation_fee2", table: "suppliers", type: "real", nullable: true, oldClrType: typeof(decimal), oldType: "numeric", oldNullable: true);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:388" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L388">20260411054624_AddPerformanceIndexes.cs:388-413</a> | AddPerformanceIndexes | Down |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:397" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L397">20260411054624_AddPerformanceIndexes.cs:397-422</a> | AddPerformanceIndexes | Down |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:464" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L464">20260411054624_AddPerformanceIndexes.cs:464-489</a> | AddPerformanceIndexes | Down |
| 4 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:612" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L612">20260411054624_AddPerformanceIndexes.cs:612-637</a> | AddPerformanceIndexes | Down |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 9: GRP-3-77219d18

**Extractability:** extractable  
**Score:** 4.16  
**Occurrences:** 4  

**Preview:**

```csharp
var srcPatterns = Regex.Matches(html, @"(?:src|data-src|data-lazy-src|data-original|data-image)=[""']([^""']+\.(?:jpg|jpeg|png|webp|gif)(?:\?[^""']*)?)[""']", RegexOptions.IgnoreCase);
foreach (Match m in srcPatterns)
    images.Add(m.Groups[1].Value);
var srcsetMatches = Regex.Matches(html, @"srcset=[""']([^""']+)[""']", RegexOptions.IgnoreCase);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:35" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L35">HtmlExtractor.cs:35-38</a> | HtmlExtractor | ExtractImageUrls |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:47" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L47">HtmlExtractor.cs:47-50</a> | HtmlExtractor | ExtractImageUrls |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:55" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L55">HtmlExtractor.cs:55-58</a> | HtmlExtractor | ExtractImageUrls |
| 4 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:58" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L58">HtmlExtractor.cs:58-61</a> | HtmlExtractor | ExtractImageUrls |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 10: GRP-6-18e4ce6d

**Extractability:** extractable  
**Score:** 4.16  
**Occurrences:** 2  

**Preview:**

```csharp
await _storage.RevokePendingAuthToken(pendingToken);
var sessionToken = GenerateSecureToken();
var expiresAt = DateTime.UtcNow.AddHours(24);
await _storage.CreateAuthSession(user.Id, sessionToken, expiresAt, HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
var cookieOptions = new CookieOptions
{
    HttpOnly = true,
    Secure = true,
    SameSite = SameSiteMode.Lax,
    Expires = expiresAt,
    Path = "/"
};
Response.Cookies.Append("ts_session", sessionToken, cookieOptions);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:102" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L102">PublicTwoFactorController.cs:102-118</a> | PublicTwoFactorController | VerifyLogin2FA |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:189" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L189">PublicTwoFactorController.cs:189-205</a> | PublicTwoFactorController | VerifySetup2FAWithToken |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 11: GRP-6-e6bf04df

**Extractability:** extractable  
**Score:** 4.16  
**Occurrences:** 2  

**Preview:**

```csharp
#pragma warning disable 612, 618
modelBuilder.HasAnnotation("ProductVersion", "8.0.11").HasAnnotation("Relational:MaxIdentifierLength", 63);
NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);
modelBuilder.Entity("TourStore.Domain.Models.AccomPricingCache", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<bool?>("Available").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("available");
    b.Property<string>("City").IsRequired().HasColumnType("text").HasColumnName("city");
    b.Property<string>("DateChecked").HasColumnType("text").HasColumnName("date_checked");
    b.Property<DateTime>("FetchedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("fetched_at").HasDefaultValueSql("now()");
    b.Property<string>("HeroProductId").IsRequired().HasColumnType("text").HasColumnName("hero_product_id");
    b.Property<decimal>("Price").ValueGeneratedOnAdd().HasColumnType("numeric").HasDefaultValue(0m).HasColumnName("price");
    b.Property<string>("RatePlanName").HasColumnType("text").HasColumnName("rate_plan_name");
    b.HasKey("Id");
    b.HasIndex("HeroProductId");
    b.ToTable("accom_pricing_cache");
});
modelBuilder.Entity("TourStore.Domain.Models.AdminNote", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Category").IsRequired().HasColumnType("text").HasColumnName("category");
    b.Property<string>("Content").IsRequired().HasColumnType("text").HasColumnName("content");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("CreatedBy").HasColumnType("text").HasColumnName("created_by");
    b.Property<bool>("IsPinned").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(false).HasColumnName("is_pinned");
    b.Property<string>("Title").IsRequired().HasColumnType("text").HasColumnName("title");
    b.Property<DateTime?>("UpdatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("updated_at").HasDefaultValueSql("now()");
    b.HasKey("Id");
    b.ToTable("admin_notes");
});
modelBuilder.Entity("TourStore.Domain.Models.AffiliateActivityLog", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Action").IsRequired().HasColumnType("text").HasColumnName("action");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<JsonElement?>("Details").HasColumnType("jsonb").HasColumnName("details");
    b.Property<string>("IpAddress").HasColumnType("text").HasColumnName("ip_address");
    b.Property<string>("PromoCode").IsRequired().HasColumnType("text").HasColumnName("promo_code");
    b.Property<string>("UserAgent").HasColumnType("text").HasColumnName("user_agent");
    b.HasKey("Id");
    b.ToTable("affiliate_activity_logs");
});
modelBuilder.Entity("TourStore.Domain.Models.AffiliateApplication", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("AboutYourself").HasColumnType("text").HasColumnName("about_yourself");
    b.Property<string>("AffiliateEntityType").HasColumnType("text").HasColumnName("affiliate_entity_type");
    b.Property<string>("AustralianBusinessNumber").HasColumnType("text").HasColumnName("australian_business_number");
    b.Property<string>("BankAccountName").IsRequired().HasColumnType("text").HasColumnName("bank_account_name");
    b.Property<string>("BankDetails1").IsRequired().HasColumnType("text").HasColumnName("bank_details_1");
    b.Property<string>("BankDetails2").IsRequired().HasColumnType("text").HasColumnName("bank_details_2");
    b.Property<string>("BankDetails3").HasColumnType("text").HasColumnName("bank_details_3");
    b.Property<string>("BusinessType").IsRequired().HasColumnType("text").HasColumnName("business_type");
    b.Property<string>("CompanyName").HasColumnType("text").HasColumnName("company_name");
    b.Property<string>("CompanyWebsite").HasColumnType("text").HasColumnName("company_website");
    b.Property<string>("Country").IsRequired().HasColumnType("text").HasColumnName("country");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<string>("Email").IsRequired().HasColumnType("text").HasColumnName("email");
    b.Property<string>("FirstName").IsRequired().HasColumnType("text").HasColumnName("first_name");
    b.Property<string>("ImageUrl").HasColumnType("text").HasColumnName("image_url");
    b.Property<string>("LastName").IsRequired().HasColumnType("text").HasColumnName("last_name");
    b.Property<string>("Notes").HasColumnType("text").HasColumnName("notes");
    b.Property<string>("Phone").HasColumnType("text").HasColumnName("phone");
    b.Property<DateTime?>("ReviewedAt").HasColumnType("timestamp with time zone").HasColumnName("reviewed_at");
    b.Property<string>("ReviewedBy").HasColumnType("text").HasColumnName("reviewed_by");
    b.Property<List<string>>("SelectedCities").IsRequired().HasColumnType("text[]").HasColumnName("selected_cities");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("pending").HasColumnName("status");
    b.Property<string>("W9Address").HasColumnType("text").HasColumnName("w9_address");
    b.Property<string>("W9BusinessName").HasColumnType("text").HasColumnName("w9_business_name");
    b.Property<bool?>("W9CertificationAgreed").HasColumnType("boolean").HasColumnName("w9_certification_agreed");
    b.Property<string>("W9City").HasColumnType("text").HasColumnName("w9_city");
    b.Property<string>("W9ExemptFatcaCode").HasColumnType("text").HasColumnName("w9_exempt_fatca_code");
    b.Property<string>("W9ExemptPayeeCode").HasColumnType("text").HasColumnName("w9_exempt_payee_code");
    b.Property<string>("W9LegalName").HasColumnType("text").HasColumnName("w9_legal_name");
    b.Property<string>("W9Signature").HasColumnType("text").HasColumnName("w9_signature");
    b.Property<DateTime?>("W9SignedAt").HasColumnType("timestamp with time zone").HasColumnName("w9_signed_at");
    b.Property<string>("W9State").HasColumnType("text").HasColumnName("w9_state");
    b.Property<string>("W9TaxClassification").HasColumnType("text").HasColumnName("w9_tax_classification");
    b.Property<string>("W9Tin").HasColumnType("text").HasColumnName("w9_tin");
    b.Property<string>("W9TinType").HasColumnType("text").HasColumnName("w9_tin_type");
    b.Property<string>("W9ZipCode").HasColumnType("text").HasColumnName("w9_zip_code");
    b.HasKey("Id");
    b.ToTable("affiliate_applications");
});
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs:24" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs#L24">20260411054624_AddPerformanceIndexes.Designer.cs:24-341</a> | AddPerformanceIndexes | BuildTargetModel |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs:21" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs#L21">AppDbContextModelSnapshot.cs:21-338</a> | AppDbContextModelSnapshot | BuildModel |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 12: GRP-5-b47e7bec

**Extractability:** extractable  
**Score:** 3.47  
**Occurrences:** 2  

**Preview:**

```csharp
var aiImages = extractedData.ContainsKey("images") && extractedData["images"] is List<object> imgs ? imgs.Select(i => i?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList() : new List<string>();
var allImages = aiImages.Union(imageUrls).Distinct().Take(10).ToList();
extractedData["images"] = allImages;
if ((!extractedData.ContainsKey("image") || extractedData["image"] == null) && allImages.Count > 0)
    extractedData["image"] = allImages[0];
extractedData["videos"] = videoUrls;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminScrapingController.cs:124" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminScrapingController.cs#L124">AdminScrapingController.cs:124-132</a> | AdminScrapingController | ScrapeUrl |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminScrapingController.cs:305" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminScrapingController.cs#L305">AdminScrapingController.cs:305-313</a> | AdminScrapingController | ScrapeUrls |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 13: GRP-3-35bb1a6b

**Extractability:** extractable  
**Score:** 3.30  
**Occurrences:** 3  

**Preview:**

```csharp
var bytes = new byte[32];
using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
rng.GetBytes(bytes);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicAuthController.cs:440" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicAuthController.cs#L440">PublicAuthController.cs:440-442</a> | PublicAuthController | GenerateSecureToken |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicAuthController.cs:451" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicAuthController.cs#L451">PublicAuthController.cs:451-453</a> | PublicAuthController | GenerateVerificationCode |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:275" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L275">PublicTwoFactorController.cs:275-277</a> | PublicTwoFactorController | GenerateSecureToken |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 14: GRP-3-c836781c

**Extractability:** extractable  
**Score:** 3.30  
**Occurrences:** 3  

**Preview:**

```csharp
modelBuilder.Entity("TourStore.Domain.Models.HeroCacheStatus", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("CacheStatus").HasColumnType("text").HasColumnName("last_refresh_status");
    b.Property<int>("DaysLoaded").HasColumnType("integer").HasColumnName("days_loaded");
    b.Property<string>("ErrorMessage").HasColumnType("text").HasColumnName("last_error");
    b.Property<string>("HeroProductId").IsRequired().HasColumnType("text").HasColumnName("hero_product_id");
    b.Property<DateTime?>("LastCacheTime").HasColumnType("timestamp with time zone").HasColumnName("last_refresh_at");
    b.Property<DateTime?>("LastPricingRefreshAt").HasColumnType("timestamp with time zone").HasColumnName("last_pricing_refresh_at");
    b.Property<DateTime?>("NextRefreshAt").HasColumnType("timestamp with time zone").HasColumnName("next_refresh_at");
    b.Property<string>("ProductId").HasColumnType("text").HasColumnName("product_id");
    b.Property<string>("ProductName").HasColumnType("text").HasColumnName("product_title");
    b.HasKey("Id");
    b.ToTable("hero_cache_status");
});
modelBuilder.Entity("TourStore.Domain.Models.HeroRequestLog", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("ChangeType").IsRequired().HasColumnType("text").HasColumnName("change_type");
    b.Property<DateTime?>("CompletedAt").HasColumnType("timestamp with time zone").HasColumnName("completed_at");
    b.Property<DateTime?>("CreatedAt").ValueGeneratedOnAdd().HasColumnType("timestamp with time zone").HasColumnName("created_at").HasDefaultValueSql("now()");
    b.Property<DateTime?>("EmailSentAt").HasColumnType("timestamp with time zone").HasColumnName("email_sent_at");
    b.Property<string>("FieldName").IsRequired().HasColumnType("text").HasColumnName("field_name");
    b.Property<string>("NewValue").HasColumnType("text").HasColumnName("new_value");
    b.Property<string>("Notes").HasColumnType("text").HasColumnName("notes");
    b.Property<string>("OldValue").HasColumnType("text").HasColumnName("old_value");
    b.Property<string>("RequestedBy").HasColumnType("text").HasColumnName("requested_by");
    b.Property<string>("Status").IsRequired().ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("pending").HasColumnName("status");
    b.Property<string>("SupplierId").IsRequired().HasColumnType("text").HasColumnName("supplier_id");
    b.Property<string>("SupplierName").IsRequired().HasColumnType("text").HasColumnName("supplier_name");
    b.HasKey("Id");
    b.ToTable("hero_request_logs");
});
modelBuilder.Entity("TourStore.Domain.Models.Keyword", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Category").ValueGeneratedOnAdd().HasColumnType("text").HasDefaultValue("Activities").HasColumnName("category");
    b.Property<bool?>("IsActive").ValueGeneratedOnAdd().HasColumnType("boolean").HasDefaultValue(true).HasColumnName("is_active");
    b.Property<string>("Name").IsRequired().HasColumnType("text").HasColumnName("name");
    b.Property<string>("ParentKeywordId").HasColumnType("text").HasColumnName("parent_keyword_id");
    b.HasKey("Id");
    b.ToTable("keywords");
});
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.Designer.cs:1650" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.Designer.cs#L1650">20260402021243_InitialCreate.Designer.cs:1650-1802</a> | InitialCreate | BuildTargetModel |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs:1652" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs#L1652">20260411054624_AddPerformanceIndexes.Designer.cs:1652-1804</a> | AddPerformanceIndexes | BuildTargetModel |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs:1652" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs#L1652">AppDbContextModelSnapshot.cs:1652-1804</a> | AppDbContextModelSnapshot | BuildModel |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 15: GRP-3-62641bff

**Extractability:** extractable  
**Score:** 3.30  
**Occurrences:** 3  

**Preview:**

```csharp
modelBuilder.Entity("TourStore.Domain.Models.Section", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("LocationId").IsRequired().HasColumnType("text").HasColumnName("location_id");
    b.Property<int>("Order").HasColumnType("integer").HasColumnName("order");
    b.Property<JsonElement>("Rules").HasColumnType("jsonb").HasColumnName("rules");
    b.Property<string>("Title").IsRequired().HasColumnType("text").HasColumnName("title");
    b.HasKey("Id");
    b.ToTable("sections");
});
modelBuilder.Entity("TourStore.Domain.Models.Session", b =>
{
    b.Property<string>("Sid").HasColumnType("text").HasColumnName("sid");
    b.Property<DateTime>("Expire").HasColumnType("timestamp with time zone").HasColumnName("expire");
    b.Property<JsonElement>("Sess").HasColumnType("jsonb").HasColumnName("sess");
    b.HasKey("Sid");
    b.HasIndex("Expire").HasDatabaseName("IDX_session_expire");
    b.ToTable("sessions");
});
modelBuilder.Entity("TourStore.Domain.Models.Setting", b =>
{
    b.Property<string>("Id").ValueGeneratedOnAdd().HasColumnType("text").HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
    b.Property<string>("Key").IsRequired().HasColumnType("text").HasColumnName("key");
    b.Property<JsonElement>("Value").HasColumnType("jsonb").HasColumnName("value");
    b.HasKey("Id");
    b.HasIndex("Key").IsUnique();
    b.ToTable("settings");
});
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.Designer.cs:3112" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.Designer.cs#L3112">20260402021243_InitialCreate.Designer.cs:3112-3188</a> | InitialCreate | BuildTargetModel |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs:3120" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.Designer.cs#L3120">20260411054624_AddPerformanceIndexes.Designer.cs:3120-3196</a> | AddPerformanceIndexes | BuildTargetModel |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs:3128" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/AppDbContextModelSnapshot.cs#L3128">AppDbContextModelSnapshot.cs:3128-3204</a> | AppDbContextModelSnapshot | BuildModel |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 16: GRP-3-126b0e9c

**Extractability:** likely_extractable  
**Score:** 2.91  
**Occurrences:** 4  

**Preview:**

```csharp
html = Regex.Replace(html, @"<!--[\s\S]*?-->", " ", RegexOptions.IgnoreCase);
// Convert structural elements to readable text markers
html = Regex.Replace(html, @"<h[1-6][^>]*>", "\n## ", RegexOptions.IgnoreCase);
html = Regex.Replace(html, @"</h[1-6]>", "\n", RegexOptions.IgnoreCase);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:192" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L192">HtmlExtractor.cs:192-196</a> | HtmlExtractor | CleanHtmlForAi |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:195" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L195">HtmlExtractor.cs:195-197</a> | HtmlExtractor | CleanHtmlForAi |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:196" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L196">HtmlExtractor.cs:196-198</a> | HtmlExtractor | CleanHtmlForAi |
| 4 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:197" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L197">HtmlExtractor.cs:197-199</a> | HtmlExtractor | CleanHtmlForAi |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | warn | block produces 1 outward variable; extractable with a return value |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 17: GRP-6-e53bd731

**Extractability:** likely_extractable  
**Score:** 2.91  
**Occurrences:** 2  

**Preview:**

```csharp
// Remove entire blocks that are noise: scripts, styles, hidden/tracking elements
html = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
html = Regex.Replace(html, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
html = Regex.Replace(html, @"<noscript[\s\S]*?</noscript>", " ", RegexOptions.IgnoreCase);
html = Regex.Replace(html, @"<svg[\s\S]*?</svg>", " ", RegexOptions.IgnoreCase);
html = Regex.Replace(html, @"<!--[\s\S]*?-->", " ", RegexOptions.IgnoreCase);
// Convert structural elements to readable text markers
html = Regex.Replace(html, @"<h[1-6][^>]*>", "\n## ", RegexOptions.IgnoreCase);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:188" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L188">HtmlExtractor.cs:188-195</a> | HtmlExtractor | CleanHtmlForAi |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:199" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L199">HtmlExtractor.cs:199-206</a> | HtmlExtractor | CleanHtmlForAi |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | warn | block produces 1 outward variable; extractable with a return value |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 18: GRP-4-d8248295

**Extractability:** extractable  
**Score:** 2.77  
**Occurrences:** 2  

**Preview:**

```csharp
migrationBuilder.AlterColumn<decimal>(name: "cancellation_fee3", table: "suppliers", type: "numeric", nullable: true, oldClrType: typeof(float), oldType: "real", oldNullable: true);
migrationBuilder.AlterColumn<decimal>(name: "cancellation_fee2", table: "suppliers", type: "numeric", nullable: true, oldClrType: typeof(float), oldType: "real", oldNullable: true);
migrationBuilder.AlterColumn<decimal>(name: "cancellation_fee1", table: "suppliers", type: "numeric", nullable: true, oldClrType: typeof(float), oldType: "real", oldNullable: true);
migrationBuilder.AlterColumn<decimal>(name: "total_spend", table: "supplier_agreements", type: "numeric", nullable: false, oldClrType: typeof(float), oldType: "real");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:22" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L22">20260411054624_AddPerformanceIndexes.cs:22-55</a> | AddPerformanceIndexes | Up |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:237" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L237">20260411054624_AddPerformanceIndexes.cs:237-270</a> | AddPerformanceIndexes | Up |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 19: GRP-4-d2b6471b

**Extractability:** extractable  
**Score:** 2.77  
**Occurrences:** 2  

**Preview:**

```csharp
migrationBuilder.AlterColumn<decimal>(name: "cancellation_fee1_override", table: "products", type: "numeric", nullable: true, oldClrType: typeof(float), oldType: "real", oldNullable: true);
migrationBuilder.AlterColumn<decimal>(name: "amount", table: "payments", type: "numeric", nullable: false, oldClrType: typeof(float), oldType: "real");
migrationBuilder.AlterColumn<decimal>(name: "processing_fee_percent", table: "locations", type: "numeric", nullable: true, defaultValue: 3m, oldClrType: typeof(float), oldType: "real", oldNullable: true, oldDefaultValue: 3f);
migrationBuilder.AlterColumn<decimal>(name: "base_price", table: "hero_availability_cache", type: "numeric", nullable: true, oldClrType: typeof(float), oldType: "real", oldNullable: true);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:255" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L255">20260411054624_AddPerformanceIndexes.cs:255-290</a> | AddPerformanceIndexes | Up |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:292" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L292">20260411054624_AddPerformanceIndexes.cs:292-327</a> | AddPerformanceIndexes | Up |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 20: GRP-4-70dc1a37

**Extractability:** extractable  
**Score:** 2.77  
**Occurrences:** 2  

**Preview:**

```csharp
migrationBuilder.AlterColumn<decimal>(name: "total_spend", table: "supplier_agreements", type: "numeric", nullable: false, oldClrType: typeof(float), oldType: "real");
migrationBuilder.AlterColumn<decimal>(name: "net_receive", table: "supplier_agreements", type: "numeric", nullable: false, oldClrType: typeof(float), oldType: "real");
migrationBuilder.AlterColumn<decimal>(name: "credit_card_fee", table: "supplier_agreements", type: "numeric", nullable: false, oldClrType: typeof(float), oldType: "real");
migrationBuilder.AlterColumn<decimal>(name: "commission_rate", table: "supplier_agreements", type: "numeric", nullable: false, oldClrType: typeof(float), oldType: "real");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:49" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L49">20260411054624_AddPerformanceIndexes.cs:49-79</a> | AddPerformanceIndexes | Up |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:127" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L127">20260411054624_AddPerformanceIndexes.cs:127-157</a> | AddPerformanceIndexes | Up |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 21: GRP-4-be11bbc2

**Extractability:** extractable  
**Score:** 2.77  
**Occurrences:** 2  

**Preview:**

```csharp
migrationBuilder.AlterColumn<float>(name: "cancellation_fee3", table: "suppliers", type: "real", nullable: true, oldClrType: typeof(decimal), oldType: "numeric", oldNullable: true);
migrationBuilder.AlterColumn<float>(name: "cancellation_fee2", table: "suppliers", type: "real", nullable: true, oldClrType: typeof(decimal), oldType: "numeric", oldNullable: true);
migrationBuilder.AlterColumn<float>(name: "cancellation_fee1", table: "suppliers", type: "real", nullable: true, oldClrType: typeof(decimal), oldType: "numeric", oldNullable: true);
migrationBuilder.AlterColumn<float>(name: "total_spend", table: "supplier_agreements", type: "real", nullable: false, oldClrType: typeof(decimal), oldType: "numeric");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:397" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L397">20260411054624_AddPerformanceIndexes.cs:397-430</a> | AddPerformanceIndexes | Down |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:612" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L612">20260411054624_AddPerformanceIndexes.cs:612-645</a> | AddPerformanceIndexes | Down |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 22: GRP-4-69361f1f

**Extractability:** extractable  
**Score:** 2.77  
**Occurrences:** 2  

**Preview:**

```csharp
migrationBuilder.AlterColumn<float>(name: "cancellation_fee1_override", table: "products", type: "real", nullable: true, oldClrType: typeof(decimal), oldType: "numeric", oldNullable: true);
migrationBuilder.AlterColumn<float>(name: "amount", table: "payments", type: "real", nullable: false, oldClrType: typeof(decimal), oldType: "numeric");
migrationBuilder.AlterColumn<float>(name: "processing_fee_percent", table: "locations", type: "real", nullable: true, defaultValue: 3f, oldClrType: typeof(decimal), oldType: "numeric", oldNullable: true, oldDefaultValue: 3m);
migrationBuilder.AlterColumn<float>(name: "base_price", table: "hero_availability_cache", type: "real", nullable: true, oldClrType: typeof(decimal), oldType: "numeric", oldNullable: true);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:630" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L630">20260411054624_AddPerformanceIndexes.cs:630-665</a> | AddPerformanceIndexes | Down |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:667" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L667">20260411054624_AddPerformanceIndexes.cs:667-702</a> | AddPerformanceIndexes | Down |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 23: GRP-4-d82d9528

**Extractability:** extractable  
**Score:** 2.77  
**Occurrences:** 2  

**Preview:**

```csharp
migrationBuilder.AlterColumn<float>(name: "total_spend", table: "supplier_agreements", type: "real", nullable: false, oldClrType: typeof(decimal), oldType: "numeric");
migrationBuilder.AlterColumn<float>(name: "net_receive", table: "supplier_agreements", type: "real", nullable: false, oldClrType: typeof(decimal), oldType: "numeric");
migrationBuilder.AlterColumn<float>(name: "credit_card_fee", table: "supplier_agreements", type: "real", nullable: false, oldClrType: typeof(decimal), oldType: "numeric");
migrationBuilder.AlterColumn<float>(name: "commission_rate", table: "supplier_agreements", type: "real", nullable: false, oldClrType: typeof(decimal), oldType: "numeric");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:424" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L424">20260411054624_AddPerformanceIndexes.cs:424-454</a> | AddPerformanceIndexes | Down |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs:502" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260411054624_AddPerformanceIndexes.cs#L502">20260411054624_AddPerformanceIndexes.cs:502-532</a> | AddPerformanceIndexes | Down |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 24: GRP-4-d42f4886

**Extractability:** discovery_only  
**Score:** 2.50  
**Occurrences:** 8  

**Preview:**

```csharp
if (error != null || response == null)
    return null;
if (!response.IsSuccessStatusCode)
    return null;
var responseBody = await response.Content.ReadAsStringAsync();
return JsonSerializer.Deserialize<JsonElement>(responseBody);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:197" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L197">HeroApiService.cs:197-204</a> | HeroApiService | CreateQuote |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:213" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L213">HeroApiService.cs:213-220</a> | HeroApiService | CreateDraftBooking |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:229" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L229">HeroApiService.cs:229-236</a> | HeroApiService | FinalizeBooking |
| 4 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:244" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L244">HeroApiService.cs:244-251</a> | HeroApiService | GetBookingDetails |
| 5 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:264" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L264">HeroApiService.cs:264-271</a> | HeroApiService | GetProductPricing |
| 6 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:279" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L279">HeroApiService.cs:279-286</a> | HeroApiService | GetProductDetails |
| 7 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:294" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L294">HeroApiService.cs:294-301</a> | HeroApiService | GetRaw |
| 8 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:309" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L309">HeroApiService.cs:309-316</a> | HeroApiService | PostRaw |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 25: GRP-11-51de466a

**Extractability:** discovery_only  
**Score:** 2.29  
**Occurrences:** 2  

**Preview:**

```csharp
if (string.IsNullOrWhiteSpace(query))
    return BadRequest(new { error = "Query is required" });
var googleApiKey = Environment.GetEnvironmentVariable("GOOGLE_PLACES_API_KEY");
if (string.IsNullOrEmpty(googleApiKey))
    return BadRequest(new { error = "Google Places API key not configured", message = "Please add GOOGLE_PLACES_API_KEY to your environment secrets" });
using var client = new HttpClient();
var requestBody = JsonSerializer.Serialize(new { textQuery = query });
var request = new HttpRequestMessage(HttpMethod.Post, "https://places.googleapis.com/v1/places:searchText")
{
    Content = new StringContent(requestBody, System.Text.Encoding.UTF8, "application/json")
};
request.Headers.Add("X-Goog-Api-Key", googleApiKey);
request.Headers.Add("X-Goog-FieldMask", "places.id,places.displayName,places.formattedAddress,places.rating,places.userRatingCount,places.addressComponents");
var response = await client.SendAsync(request);
var responseBody = await response.Content.ReadAsStringAsync();
var data = JsonSerializer.Deserialize<JsonElement>(responseBody);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs:18" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs#L18">AdminGooglePlacesController.cs:18-36</a> | AdminGooglePlacesController | SearchGooglePlaces |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs:85" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs#L85">AdminGooglePlacesController.cs:85-103</a> | AdminGooglePlacesController | GeocodeLocation |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | warn | block produces 1 outward variable; extractable with a return value |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 26: GRP-3-a50846e5

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
int linked = 0;
int alreadyLinked = 0;
int noHeroMatch = 0;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminSuppliersController.cs:123" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminSuppliersController.cs#L123">AdminSuppliersController.cs:123-125</a> | AdminSuppliersController | RelinkProductsToBranches |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminSuppliersController.cs:124" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminSuppliersController.cs#L124">AdminSuppliersController.cs:124-126</a> | AdminSuppliersController | RelinkProductsToBranches |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 27: GRP-3-b178aff0

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
string countryCode = "";
string countryName = "";
string city = "";
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/HeroGeoController.cs:36" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/HeroGeoController.cs#L36">HeroGeoController.cs:36-38</a> | HeroGeoController | GeoDetect |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/HeroGeoController.cs:37" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/HeroGeoController.cs#L37">HeroGeoController.cs:37-39</a> | HeroGeoController | GeoDetect |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 28: GRP-3-ad275ac6

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
var recoveryCodes = GenerateRecoveryCodes(8);
var hashedCodes = string.Join(",", recoveryCodes.Select(c => BCrypt.Net.BCrypt.HashPassword(c)));
await _storage.EnableTotp(user.Id, hashedCodes);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:56" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L56">PublicTwoFactorController.cs:56-59</a> | PublicTwoFactorController | VerifySetup2FA |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:185" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L185">PublicTwoFactorController.cs:185-187</a> | PublicTwoFactorController | VerifySetup2FAWithToken |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 29: GRP-3-0e2224c8

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
migrationBuilder.CreateIndex(name: "IX_auth_sessions_user_id", table: "auth_sessions", column: "user_id");
migrationBuilder.CreateIndex(name: "IX_email_templates_slug", table: "email_templates", column: "slug", unique: true);
migrationBuilder.CreateIndex(name: "IX_embed_widgets_code", table: "embed_widgets", column: "code", unique: true);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.cs:1251" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.cs#L1251">20260402021243_InitialCreate.cs:1251-1266</a> | InitialCreate | Up |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.cs:1280" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Data/Migrations/20260402021243_InitialCreate.cs#L1280">20260402021243_InitialCreate.cs:1280-1295</a> | InitialCreate | Up |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 30: GRP-3-687d95d8

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
var ytEmbeds = Regex.Matches(html, @"(?:youtube\.com/embed/|youtube-nocookie\.com/embed/)([a-zA-Z0-9_-]{11})", RegexOptions.IgnoreCase);
foreach (Match m in ytEmbeds)
    videos.Add($"https://www.youtube.com/watch?v={m.Groups[1].Value}");
var ytLinks = Regex.Matches(html, @"(?:youtube\.com/watch\?v=|youtu\.be/)([a-zA-Z0-9_-]{11})", RegexOptions.IgnoreCase);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:114" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L114">HtmlExtractor.cs:114-117</a> | HtmlExtractor | ExtractVideoUrls |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:117" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L117">HtmlExtractor.cs:117-120</a> | HtmlExtractor | ExtractVideoUrls |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 31: GRP-3-81316eae

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
context.Response.Headers["X-Content-Type-Options"] = "nosniff";
context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Middleware/SecurityMiddleware.cs:115" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Middleware/SecurityMiddleware.cs#L115">SecurityMiddleware.cs:115-117</a> | SecurityHeadersMiddleware | InvokeAsync |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Middleware/SecurityMiddleware.cs:116" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Middleware/SecurityMiddleware.cs#L116">SecurityMiddleware.cs:116-118</a> | SecurityHeadersMiddleware | InvokeAsync |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 32: GRP-3-b25da21d

**Extractability:** extractable  
**Score:** 2.08  
**Occurrences:** 2  

**Preview:**

```csharp
var attachedAssetsPath = Path.Combine(root, "attached_assets");
Directory.CreateDirectory(attachedAssetsPath);
app.UseStaticFiles(new StaticFileOptions { FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(attachedAssetsPath), RequestPath = "/attached_assets", OnPrepareResponse = ctx => ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=604800") });
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Middleware/StaticFilesExtensions.cs:9" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Middleware/StaticFilesExtensions.cs#L9">StaticFilesExtensions.cs:9-17</a> | StaticFilesExtensions | UseLocalStaticFiles |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Middleware/StaticFilesExtensions.cs:19" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Middleware/StaticFilesExtensions.cs#L19">StaticFilesExtensions.cs:19-27</a> | StaticFilesExtensions | UseLocalStaticFiles |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 33: GRP-5-706c73cc

**Extractability:** discovery_only  
**Score:** 2.08  
**Occurrences:** 4  

**Preview:**

```csharp
var(response, error) = await MakeRequest(url, HttpMethod.Post, body, apiKey);
if (error != null || response == null)
    return null;
if (!response.IsSuccessStatusCode)
    return null;
var responseBody = await response.Content.ReadAsStringAsync();
return JsonSerializer.Deserialize<JsonElement>(responseBody);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:195" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L195">HeroApiService.cs:195-204</a> | HeroApiService | CreateQuote |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:211" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L211">HeroApiService.cs:211-220</a> | HeroApiService | CreateDraftBooking |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:227" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L227">HeroApiService.cs:227-236</a> | HeroApiService | FinalizeBooking |
| 4 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:262" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L262">HeroApiService.cs:262-271</a> | HeroApiService | GetProductPricing |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 34: GRP-6-f0efe3e7

**Extractability:** discovery_only  
**Score:** 1.98  
**Occurrences:** 3  

**Preview:**

```csharp
var body = JsonSerializer.Serialize(quoteRequest);
var(response, error) = await MakeRequest(url, HttpMethod.Post, body, apiKey);
if (error != null || response == null)
    return null;
if (!response.IsSuccessStatusCode)
    return null;
var responseBody = await response.Content.ReadAsStringAsync();
return JsonSerializer.Deserialize<JsonElement>(responseBody);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:194" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L194">HeroApiService.cs:194-204</a> | HeroApiService | CreateQuote |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:210" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L210">HeroApiService.cs:210-220</a> | HeroApiService | CreateDraftBooking |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:226" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L226">HeroApiService.cs:226-236</a> | HeroApiService | FinalizeBooking |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 35: GRP-4-e7e6a5f9

**Extractability:** likely_extractable  
**Score:** 1.94  
**Occurrences:** 2  

**Preview:**

```csharp
html = Regex.Replace(html, @"<!--[\s\S]*?-->", " ", RegexOptions.IgnoreCase);
// Convert structural elements to readable text markers
html = Regex.Replace(html, @"<h[1-6][^>]*>", "\n## ", RegexOptions.IgnoreCase);
html = Regex.Replace(html, @"</h[1-6]>", "\n", RegexOptions.IgnoreCase);
html = Regex.Replace(html, @"<li[^>]*>", "\n ", RegexOptions.IgnoreCase);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:192" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L192">HtmlExtractor.cs:192-197</a> | HtmlExtractor | CleanHtmlForAi |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:195" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L195">HtmlExtractor.cs:195-198</a> | HtmlExtractor | CleanHtmlForAi |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | warn | block produces 1 outward variable; extractable with a return value |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 36: GRP-9-92b70cc4

**Extractability:** discovery_only  
**Score:** 1.87  
**Occurrences:** 2  

**Preview:**

```csharp
if (string.IsNullOrWhiteSpace(placeId))
    placeId = supplier.GooglePlaceId;
if (string.IsNullOrWhiteSpace(placeId))
    return BadRequest(new { error = "googlePlaceId is required" });
var(reviewData, error) = await FetchPlaceReviews(placeId, includeAddressComponents: true);
if (error != null)
    return BadRequest(new { error });
var cachedReviews = new
{
    google = reviewData!.Reviews,
    overallRating = reviewData.OverallRating,
    totalReviews = reviewData.TotalReviews,
    placeName = reviewData.PlaceName,
    placeAddress = reviewData.PlaceAddress
};
var cachedJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(cachedReviews));
supplier.CachedReviews = cachedJson;
supplier.GooglePlaceId = placeId;
supplier.ReviewsLastFetched = DateTime.UtcNow;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs:162" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs#L162">AdminGooglePlacesController.cs:162-182</a> | AdminGooglePlacesController | FetchSupplierReviews |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs:206" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs#L206">AdminGooglePlacesController.cs:206-226</a> | AdminGooglePlacesController | FetchBranchReviews |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | fail | block produces 2 outward variables; would need tuple return or out-params |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | warn | captured fields/properties differ across occurrences; review before extraction |

## Group 37: GRP-4-92292f11

**Extractability:** discovery_only  
**Score:** 1.66  
**Occurrences:** 4  

**Preview:**

```csharp
var(response, error) = await MakeRequest(url, HttpMethod.Get, apiKey: apiKey);
if (error != null || response == null)
    return null;
if (!response.IsSuccessStatusCode)
    return null;
var responseBody = await response.Content.ReadAsStringAsync();
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:176" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L176">HeroApiService.cs:176-184</a> | HeroApiService | GetAvailability |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:242" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L242">HeroApiService.cs:242-250</a> | HeroApiService | GetBookingDetails |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:277" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L277">HeroApiService.cs:277-285</a> | HeroApiService | GetProductDetails |
| 4 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:292" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L292">HeroApiService.cs:292-300</a> | HeroApiService | GetRaw |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 38: GRP-5-96c383d6

**Extractability:** discovery_only  
**Score:** 1.65  
**Occurrences:** 3  

**Preview:**

```csharp
var(response, error) = await MakeRequest(url, HttpMethod.Get, apiKey: apiKey);
if (error != null || response == null)
    return null;
if (!response.IsSuccessStatusCode)
    return null;
var responseBody = await response.Content.ReadAsStringAsync();
return JsonSerializer.Deserialize<JsonElement>(responseBody);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:242" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L242">HeroApiService.cs:242-251</a> | HeroApiService | GetBookingDetails |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:277" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L277">HeroApiService.cs:277-286</a> | HeroApiService | GetProductDetails |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:292" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L292">HeroApiService.cs:292-301</a> | HeroApiService | GetRaw |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 39: GRP-3-e0a0e80f

**Extractability:** likely_extractable  
**Score:** 1.46  
**Occurrences:** 2  

**Preview:**

```csharp
var locality = GetComponent("locality");
var adminArea2 = GetComponent("administrative_area_level_2");
var adminArea1 = GetComponent("administrative_area_level_1");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs:127" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs#L127">AdminGooglePlacesController.cs:127-129</a> | AdminGooglePlacesController | GeocodeLocation |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs:128" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs#L128">AdminGooglePlacesController.cs:128-130</a> | AdminGooglePlacesController | GeocodeLocation |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | warn | block produces 1 outward variable; extractable with a return value |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 40: GRP-3-8ea4fc11

**Extractability:** likely_extractable  
**Score:** 1.46  
**Occurrences:** 2  

**Preview:**

```csharp
if (string.IsNullOrEmpty(approval.Id))
    approval.Id = Guid.NewGuid().ToString();
approval.ProductId = productId;
approval.CreatedAt = DateTime.UtcNow;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminProductApprovalsController.cs:33" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminProductApprovalsController.cs#L33">AdminProductApprovalsController.cs:33-35</a> | AdminProductApprovalsController | CreateSupplierProductApproval |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminProductCommentsController.cs:26" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminProductCommentsController.cs#L26">AdminProductCommentsController.cs:26-28</a> | AdminProductCommentsController | CreateSupplierProductComment |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | warn | captured fields/properties differ across occurrences; review before extraction |

## Group 41: GRP-3-83c5f3fe

**Extractability:** likely_extractable  
**Score:** 1.46  
**Occurrences:** 2  

**Preview:**

```csharp
result = Regex.Replace(result, @"<style[^>]*>[\s\S]*?</style>", "", RegexOptions.IgnoreCase);
result = Regex.Replace(result, @"<nav[^>]*>[\s\S]*?</nav>", "", RegexOptions.IgnoreCase);
result = Regex.Replace(result, @"<footer[^>]*>[\s\S]*?</footer>", "", RegexOptions.IgnoreCase);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:230" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L230">HtmlExtractor.cs:230-232</a> | HtmlExtractor | StripHtml |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs:231" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HtmlExtractor.cs#L231">HtmlExtractor.cs:231-233</a> | HtmlExtractor | StripHtml |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | warn | block produces 1 outward variable; extractable with a return value |
| no-control-flow-escape | pass |  |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 42: GRP-3-3c55ff1b

**Extractability:** discovery_only  
**Score:** 1.45  
**Occurrences:** 5  

**Preview:**

```csharp
user.UpdatedAt = DateTime.UtcNow;
await _db.SaveChangesAsync();
return user;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.Settings.cs:55" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.Settings.cs#L55">StorageService.Settings.cs:55-57</a> | StorageService | LinkSsoToUser |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs:15" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs#L15">StorageService.TwoFactor.cs:15-17</a> | StorageService | SetTotpSecret |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs:26" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs#L26">StorageService.TwoFactor.cs:26-28</a> | StorageService | EnableTotp |
| 4 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs:38" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs#L38">StorageService.TwoFactor.cs:38-40</a> | StorageService | DisableTotp |
| 5 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs:48" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs#L48">StorageService.TwoFactor.cs:48-50</a> | StorageService | UpdateRecoveryCodes |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 43: GRP-6-5d6323be

**Extractability:** discovery_only  
**Score:** 1.25  
**Occurrences:** 2  

**Preview:**

```csharp
var(response, apiError) = await MakeRequest(url, HttpMethod.Get);
if (apiError != null || response == null)
    return (null, apiError ?? "Failed to connect to Hero API");
var responseBody = await response.Content.ReadAsStringAsync();
if (string.IsNullOrWhiteSpace(responseBody))
    return (new List<JsonElement>(), null);
if (responseBody.TrimStart().StartsWith("<!DOCTYPE") || responseBody.TrimStart().StartsWith("<html"))
    return (null, "API authentication failed. The Hero API key may be expired or invalid. Please contact Hero support at api@hero.travel to verify your API credentials.");
if (!response.IsSuccessStatusCode)
    return (new List<JsonElement>(), null);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:335" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L335">HeroApiService.cs:335-349</a> | HeroApiService | SearchSuppliers |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs:360" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/HeroApiService.cs#L360">HeroApiService.cs:360-374</a> | HeroApiService | SearchProducts |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 44: GRP-5-6baf61fc

**Extractability:** discovery_only  
**Score:** 1.04  
**Occurrences:** 2  

**Preview:**

```csharp
if (string.IsNullOrEmpty(pendingToken))
    return BadRequest(new { error = "Missing pending authentication token" });
if (string.IsNullOrEmpty(code))
    return BadRequest(new { error = "Missing verification code" });
var pending = await _storage.GetPendingAuthToken(pendingToken);
if (pending == null)
    return Unauthorized(new { error = "Invalid or expired pending token. Please sign in again." });
var user = await _storage.GetUser(pending.UserId);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:72" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L72">PublicTwoFactorController.cs:72-81</a> | PublicTwoFactorController | VerifyLogin2FA |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:166" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L166">PublicTwoFactorController.cs:166-175</a> | PublicTwoFactorController | VerifySetup2FAWithToken |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 45: GRP-5-a0c0c5be

**Extractability:** discovery_only  
**Score:** 1.04  
**Occurrences:** 2  

**Preview:**

```csharp
var existing = await storage.GetSupplier(id);
if (existing == null)
    return NotFound(new { error = "Supplier not found" });
existing.Status = "Archived";
var result = await storage.UpdateSupplier(id, existing);
return Ok(result);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminSuppliersController.cs:53" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminSuppliersController.cs#L53">AdminSuppliersController.cs:53-58</a> | AdminSuppliersController | ArchiveSupplier |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminSuppliersController.cs:64" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminSuppliersController.cs#L64">AdminSuppliersController.cs:64-69</a> | AdminSuppliersController | UnarchiveSupplier |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 46: GRP-5-1fbec8c3

**Extractability:** discovery_only  
**Score:** 1.04  
**Occurrences:** 2  

**Preview:**

```csharp
var user = await GetAuthenticatedUser();
if (user == null)
    return Unauthorized(new { error = "Not authenticated" });
var code = body.TryGetProperty("code", out var codeVal) ? codeVal.GetString() : null;
if (string.IsNullOrEmpty(code))
    return BadRequest(new { error = "Missing verification code" });
var fullUser = await _storage.GetUser(user.Id);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:42" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L42">PublicTwoFactorController.cs:42-49</a> | PublicTwoFactorController | VerifySetup2FA |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:229" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L229">PublicTwoFactorController.cs:229-236</a> | PublicTwoFactorController | Disable2FA |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | fail | block passes arguments with ref/out/in modifiers |
| consistent-captured-state | pass |  |

## Group 47: GRP-5-90379673

**Extractability:** discovery_only  
**Score:** 1.04  
**Occurrences:** 2  

**Preview:**

```csharp
if (user == null)
    return Unauthorized(new { error = "Not authenticated" });
var secret = Base32Encoding.ToString(KeyGeneration.GenerateRandomKey(20));
await _storage.SetTotpSecret(user.Id, secret);
var issuer = "TourStore";
var otpauthUri = $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(user.Email ?? user.Name ?? "admin")}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&digits=6&period=30";
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:26" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L26">PublicTwoFactorController.cs:26-33</a> | PublicTwoFactorController | Setup2FA |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:147" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L147">PublicTwoFactorController.cs:147-154</a> | PublicTwoFactorController | Setup2FAWithToken |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 48: GRP-3-85fd47f6

**Extractability:** discovery_only  
**Score:** 0.99  
**Occurrences:** 3  

**Preview:**

```csharp
if (string.IsNullOrWhiteSpace(placeId))
    return BadRequest(new { error = "googlePlaceId is required" });
var(reviewData, error) = await FetchPlaceReviews(placeId, includeAddressComponents: true);
if (error != null)
    return BadRequest(new { error });
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs:164" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs#L164">AdminGooglePlacesController.cs:164-168</a> | AdminGooglePlacesController | FetchSupplierReviews |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs:208" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs#L208">AdminGooglePlacesController.cs:208-212</a> | AdminGooglePlacesController | FetchBranchReviews |
| 3 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs:239" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminGooglePlacesController.cs#L239">AdminGooglePlacesController.cs:239-243</a> | AdminGooglePlacesController | GetPlaceDetails |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | warn | block produces 1 outward variable; extractable with a return value |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 49: GRP-4-859e336a

**Extractability:** discovery_only  
**Score:** 0.83  
**Occurrences:** 2  

**Preview:**

```csharp
var location = await _storage.GetLocation(locationId);
if (location == null)
    return (null, "Location not found", 404);
var(baseUrl, apiKey) = await _ai.GetCredentialsAsync();
if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
    return (null, "No AI API key configured. Please add one in Settings.", 500);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/AiContentService/AiContentService.cs:37" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/AiContentService/AiContentService.cs#L37">AiContentService.cs:37-42</a> | AiContentService | DetectPickupAreasAsync |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/AiContentService/AiContentService.cs:198" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/AiContentService/AiContentService.cs#L198">AiContentService.cs:198-203</a> | AiContentService | GenerateFromBlogThemeAsync |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | fail | block produces 2 outward variables; would need tuple return or out-params |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 50: GRP-4-e9a8b9a3

**Extractability:** discovery_only  
**Score:** 0.83  
**Occurrences:** 2  

**Preview:**

```csharp
var bytes = new byte[32];
using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
rng.GetBytes(bytes);
return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicAuthController.cs:440" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicAuthController.cs#L440">PublicAuthController.cs:440-443</a> | PublicAuthController | GenerateSecureToken |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:275" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L275">PublicTwoFactorController.cs:275-278</a> | PublicTwoFactorController | GenerateSecureToken |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 51: GRP-4-2d9822d9

**Extractability:** discovery_only  
**Score:** 0.83  
**Occurrences:** 2  

**Preview:**

```csharp
var pending = await _storage.GetPendingAuthToken(pendingToken);
if (pending == null)
    return Unauthorized(new { error = "Invalid or expired token. Please sign in again." });
var user = await _storage.GetUser(pending.UserId);
if (user == null)
    return Unauthorized(new { error = "Invalid or expired token. Please sign in again." });
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:142" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L142">PublicTwoFactorController.cs:142-148</a> | PublicTwoFactorController | Setup2FAWithToken |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:171" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L171">PublicTwoFactorController.cs:171-177</a> | PublicTwoFactorController | VerifySetup2FAWithToken |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 52: GRP-4-a2483f0f

**Extractability:** discovery_only  
**Score:** 0.83  
**Occurrences:** 2  

**Preview:**

```csharp
user.RecoveryCodes = recoveryCodes;
user.UpdatedAt = DateTime.UtcNow;
await _db.SaveChangesAsync();
return user;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs:25" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs#L25">StorageService.TwoFactor.cs:25-28</a> | StorageService | EnableTotp |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs:47" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs#L47">StorageService.TwoFactor.cs:47-50</a> | StorageService | UpdateRecoveryCodes |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 53: GRP-3-4f8b5b55

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
var urls = new List<string>();
if (body.TryGetProperty("urls", out var urlsProp) && urlsProp.ValueKind == JsonValueKind.Array)
{
    foreach (var u in urlsProp.EnumerateArray())
    {
        var s = u.GetString();
        if (!string.IsNullOrEmpty(s))
            urls.Add(s);
    }
}
if (urls.Count == 0)
    return BadRequest(new { error = "At least one URL is required" });
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminScrapingController.cs:140" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminScrapingController.cs#L140">AdminScrapingController.cs:140-150</a> | AdminScrapingController | ExtractBranchImages |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminScrapingController.cs:200" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminScrapingController.cs#L200">AdminScrapingController.cs:200-211</a> | AdminScrapingController | ScrapeUrls |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | fail | block passes arguments with ref/out/in modifiers |
| consistent-captured-state | pass |  |

## Group 54: GRP-3-83bec26a

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
var(aiBaseUrl, aiApiKey) = await _ai.GetCredentialsAsync();
if (string.IsNullOrEmpty(aiBaseUrl) || string.IsNullOrEmpty(aiApiKey))
    return StatusCode(500, new { error = "AI API key not configured" });
var existingKeywords = await _storage.GetKeywords();
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminLocationsController.cs:184" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminLocationsController.cs#L184">AdminLocationsController.cs:184-188</a> | AdminLocationsController | ExtractKeywordsFromText |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminLocationsController.cs:196" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Admin/AdminLocationsController.cs#L196">AdminLocationsController.cs:196-200</a> | AdminLocationsController | ScanProductsForKeywords |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 55: GRP-3-300ad802

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
var result = await _heroApi.CreateDraftBooking(body);
if (result == null)
    return StatusCode(500, new { error = "Failed to create Hero booking" });
return Ok(result);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/HeroApiController.cs:663" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/HeroApiController.cs#L663">HeroApiController.cs:663-667</a> | HeroApiController | CreateHeroBooking |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/HeroApiController.cs:831" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/HeroApiController.cs#L831">HeroApiController.cs:831-835</a> | HeroApiController | CreateHeroDraftBooking |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 56: GRP-3-528fbba7

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
var result = await heroApi.CreateQuote(body);
if (result == null)
    return StatusCode(500, new { error = "Failed to create Hero quote" });
return Ok(result);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/ItineraryController.cs:490" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/ItineraryController.cs#L490">ItineraryController.cs:490-494</a> | ItineraryController | CreateHeroQuoteForItinerary |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/ItineraryController.cs:555" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/ItineraryController.cs#L555">ItineraryController.cs:555-559</a> | ItineraryController | CreateHeroQuoteAuthenticated |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 57: GRP-3-ca02afdb

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
var result = await heroApi.CreateDraftBooking(body);
if (result == null)
    return StatusCode(500, new { error = "Failed to create Hero booking" });
return Ok(result);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/ItineraryController.cs:509" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/ItineraryController.cs#L509">ItineraryController.cs:509-513</a> | ItineraryController | CreateHeroBookingForItinerary |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/ItineraryController.cs:590" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Booking/ItineraryController.cs#L590">ItineraryController.cs:590-594</a> | ItineraryController | CreateHeroDraftBookingAuthenticated |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 58: GRP-3-cb9efc6c

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
if (!VerifyTotpCode(fullUser.TotpSecret, code))
    return BadRequest(new { error = "Invalid verification code. Please try again." });
var recoveryCodes = GenerateRecoveryCodes(8);
var hashedCodes = string.Join(",", recoveryCodes.Select(c => BCrypt.Net.BCrypt.HashPassword(c)));
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:53" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L53">PublicTwoFactorController.cs:53-57</a> | PublicTwoFactorController | VerifySetup2FA |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs:182" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Controllers/Public/PublicTwoFactorController.cs#L182">PublicTwoFactorController.cs:182-186</a> | PublicTwoFactorController | VerifySetup2FAWithToken |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 59: GRP-3-73614f0e

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
var result = await _ai.CallAsync(baseUrl, apiKey, systemPrompt, userPrompt, 2000, 0.3f);
if (result == null)
    return (null, "AI returned no content", 500);
return (result, null, 200);
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/AiContentService/AiContentService.cs:104" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/AiContentService/AiContentService.cs#L104">AiContentService.cs:104-107</a> | AiContentService | DetectPickupAreasAsync |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/AiContentService/AiContentService.cs:640" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/AiContentService/AiContentService.cs#L640">AiContentService.cs:640-643</a> | AiContentService | GenerateSeoAsync |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 60: GRP-3-8ce4d0b9

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
if (!Uri.TryCreate(urlStr, UriKind.Absolute, out var parsed))
    throw new ArgumentException($"Invalid URL: {urlStr}");
var hostname = parsed.Host.ToLower();
if (hostname == "localhost" || hostname == "127.0.0.1" || hostname == "0.0.0.0" || hostname.StartsWith("10.") || hostname.StartsWith("192.168.") || hostname.StartsWith("172.") || hostname == "[::1]" || hostname.EndsWith(".local") || hostname.EndsWith(".internal"))
    throw new ArgumentException($"Private/local URLs not allowed: {urlStr}");
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/AiContentService/AiContentService.cs:648" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/AiContentService/AiContentService.cs#L648">AiContentService.cs:648-655</a> | AiContentService | ValidateUrlNotPrivate |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/WebFetchService.cs:41" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/External/WebFetchService.cs#L41">WebFetchService.cs:41-48</a> | WebFetchService | ValidateUrlNotPrivate |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | pass |  |
| no-ref-out-params | fail | block passes arguments with ref/out/in modifiers |
| consistent-captured-state | pass |  |

## Group 61: GRP-3-7841238c

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
_db.Entry(existing).CurrentValues.SetValues(updates);
await _db.SaveChangesAsync();
return existing;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.Cache.cs:371" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.Cache.cs#L371">StorageService.Cache.cs:371-373</a> | StorageService | UpdateHeroRequestLog |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.Suppliers.cs:47" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.Suppliers.cs#L47">StorageService.Suppliers.cs:47-49</a> | StorageService | UpdateSupplier |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

## Group 62: GRP-3-11bb5ef4

**Extractability:** discovery_only  
**Score:** 0.62  
**Occurrences:** 2  

**Preview:**

```csharp
var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
if (user == null)
    return null;
user.TotpEnabled = true;
```

| # | Location | Type | Member |
| --- | --- | --- | --- |
| 1 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs:22" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs#L22">StorageService.TwoFactor.cs:22-24</a> | StorageService | EnableTotp |
| 2 | <a href="vscode://file/C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs:33" title="file:///C:/code/tour-store/src/backend/TourStore.Api/Infrastructure/Storage/StorageService.TwoFactor.cs#L33">StorageService.TwoFactor.cs:33-35</a> | StorageService | DisableTotp |

| Check | Status | Detail |
| --- | --- | --- |
| parameterizable-inputs | pass |  |
| single-output | pass |  |
| no-control-flow-escape | fail | block contains a return statement |
| no-ref-out-params | pass |  |
| consistent-captured-state | pass |  |

