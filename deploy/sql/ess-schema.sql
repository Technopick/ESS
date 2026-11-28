CREATE TABLE IF NOT EXISTS __ef_migrations_history (
    migration_id character varying(150) NOT NULL,
    product_version character varying(32) NOT NULL,
    CONSTRAINT pk___ef_migrations_history PRIMARY KEY (migration_id)
);

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE announcements (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        title character varying(256) NOT NULL,
        body_html text NOT NULL,
        publish_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone,
        target_department_id uuid,
        target_location_id uuid,
        is_pinned boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_announcements PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE approval_delegations (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        from_user_id uuid NOT NULL,
        to_user_id uuid NOT NULL,
        starts_at_utc timestamp with time zone NOT NULL,
        ends_at_utc timestamp with time zone NOT NULL,
        document_type character varying(256),
        reason text,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_approval_delegations PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE approval_workflows (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        code character varying(256) NOT NULL,
        name character varying(256) NOT NULL,
        document_type character varying(256) NOT NULL,
        owner integer NOT NULL,
        is_active boolean NOT NULL,
        version integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_approval_workflows PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE audit_logs (
        id uuid NOT NULL,
        occurred_at_utc timestamp with time zone NOT NULL,
        user_id uuid,
        user_name character varying(256),
        company_id uuid,
        action character varying(256) NOT NULL,
        entity_type character varying(256),
        entity_id character varying(256),
        changes_json text,
        success boolean NOT NULL,
        ip_address character varying(256),
        user_agent character varying(1024),
        correlation_id character varying(256),
        CONSTRAINT pk_audit_logs PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE document_access_logs (
        id uuid NOT NULL,
        document_id uuid NOT NULL,
        version integer NOT NULL,
        user_id uuid NOT NULL,
        accessed_at_utc timestamp with time zone NOT NULL,
        action character varying(256) NOT NULL,
        ip_address character varying(256),
        CONSTRAINT pk_document_access_logs PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE document_categories (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        code character varying(256) NOT NULL,
        name character varying(256) NOT NULL,
        employee_can_upload boolean NOT NULL,
        retention_months integer,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_document_categories PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE employee_qualifications (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        employee_id uuid NOT NULL,
        nav_key character varying(256) NOT NULL,
        name character varying(256) NOT NULL,
        institution character varying(256),
        is_professional_certification boolean NOT NULL,
        awarded_on date,
        expires_on date,
        document_id uuid,
        last_synced_at_utc timestamp with time zone NOT NULL,
        nav_version_token character varying(256),
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_employee_qualifications PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE expense_categories (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        code character varying(256) NOT NULL,
        name character varying(256) NOT NULL,
        nav_expense_code character varying(256),
        requires_receipt boolean NOT NULL,
        max_amount_per_line numeric(18,2),
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_expense_categories PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE integration_queue (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        operation character varying(256) NOT NULL,
        entity_type character varying(256) NOT NULL,
        entity_id uuid NOT NULL,
        idempotency_key character varying(256) NOT NULL,
        payload_json text NOT NULL,
        payload_encrypted boolean NOT NULL,
        status integer NOT NULL,
        priority integer NOT NULL,
        attempts integer NOT NULL,
        max_attempts integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        next_attempt_at_utc timestamp with time zone NOT NULL,
        locked_until_utc timestamp with time zone,
        locked_by character varying(256),
        completed_at_utc timestamp with time zone,
        nav_document_no character varying(256),
        last_error text,
        last_error_category character varying(256),
        correlation_id character varying(256),
        created_by character varying(256),
        CONSTRAINT pk_integration_queue PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE locations (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        nav_key character varying(256) NOT NULL,
        name character varying(256) NOT NULL,
        last_synced_at_utc timestamp with time zone NOT NULL,
        nav_version_token character varying(256),
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_locations PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE nav_integration_settings (
        id uuid NOT NULL,
        name character varying(256) NOT NULL,
        platform integer NOT NULL,
        server_host character varying(256),
        service_instance character varying(256),
        soap_base_url character varying(1024),
        o_data_base_url character varying(1024),
        api_base_url character varying(1024),
        environment_name character varying(256),
        tenant_id character varying(256),
        auth_method integer NOT NULL,
        user_name character varying(256),
        domain character varying(256),
        password_encrypted text,
        client_id character varying(256),
        client_secret_encrypted text,
        timeout_seconds integer NOT NULL,
        sync_interval_minutes integer NOT NULL,
        validate_server_certificate boolean NOT NULL,
        max_concurrency integer NOT NULL,
        is_enabled boolean NOT NULL,
        use_simulator boolean NOT NULL,
        last_health_check_at_utc timestamp with time zone,
        last_health_check_ok boolean,
        last_health_check_message text,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_nav_integration_settings PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE nav_sync_jobs (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        entity_name character varying(256) NOT NULL,
        is_full_sync boolean NOT NULL,
        triggered_by character varying(256) NOT NULL,
        status integer NOT NULL,
        started_at_utc timestamp with time zone NOT NULL,
        finished_at_utc timestamp with time zone,
        records_read integer NOT NULL,
        records_created integer NOT NULL,
        records_updated integer NOT NULL,
        records_deactivated integer NOT NULL,
        records_failed integer NOT NULL,
        watermark character varying(256),
        error text,
        CONSTRAINT pk_nav_sync_jobs PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE nav_sync_logs (
        id uuid NOT NULL,
        company_id uuid,
        sync_job_id uuid,
        queue_item_id uuid,
        protocol character varying(256) NOT NULL,
        operation character varying(256) NOT NULL,
        http_method character varying(256) NOT NULL,
        endpoint character varying(1024) NOT NULL,
        status_code integer,
        success boolean NOT NULL,
        duration_ms bigint NOT NULL,
        request_masked text,
        response_masked text,
        error text,
        correlation_id character varying(256),
        occurred_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_nav_sync_logs PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE notification_templates (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        event_code character varying(256) NOT NULL,
        channel integer NOT NULL,
        subject character varying(256),
        body text NOT NULL,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_notification_templates PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE payroll_periods_cache (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        nav_key character varying(256) NOT NULL,
        name character varying(256) NOT NULL,
        start_date date NOT NULL,
        end_date date NOT NULL,
        pay_date date,
        is_closed boolean NOT NULL,
        is_published boolean NOT NULL,
        published_at_utc timestamp with time zone,
        last_synced_at_utc timestamp with time zone NOT NULL,
        nav_version_token character varying(256),
        CONSTRAINT pk_payroll_periods_cache PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE permissions (
        id uuid NOT NULL,
        code character varying(256) NOT NULL,
        module character varying(256) NOT NULL,
        description text,
        CONSTRAINT pk_permissions PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE positions (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        nav_key character varying(256) NOT NULL,
        title character varying(256) NOT NULL,
        department_id uuid,
        last_synced_at_utc timestamp with time zone NOT NULL,
        nav_version_token character varying(256),
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_positions PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE public_holidays (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        date date NOT NULL,
        name character varying(256) NOT NULL,
        nav_calendar_code character varying(256),
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_public_holidays PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE reference_sequences (
        company_id uuid NOT NULL,
        prefix character varying(256) NOT NULL,
        year integer NOT NULL,
        last_value bigint NOT NULL,
        CONSTRAINT pk_reference_sequences PRIMARY KEY (company_id, prefix, year)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE roles (
        id uuid NOT NULL,
        name character varying(256) NOT NULL,
        normalized_name character varying(256) NOT NULL,
        description text,
        is_system boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_roles PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE shifts (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        code character varying(256) NOT NULL,
        name character varying(256) NOT NULL,
        start_time time without time zone NOT NULL,
        end_time time without time zone NOT NULL,
        grace_minutes integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_shifts PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE system_settings (
        id uuid NOT NULL,
        company_id uuid,
        key character varying(256) NOT NULL,
        value text NOT NULL,
        is_secret boolean NOT NULL,
        description text,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_system_settings PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE training_programs (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        code character varying(256) NOT NULL,
        title character varying(256) NOT NULL,
        description text,
        provider character varying(256),
        starts_at_utc timestamp with time zone,
        ends_at_utc timestamp with time zone,
        venue character varying(256),
        capacity integer,
        cost_per_participant numeric(18,2),
        requires_approval boolean NOT NULL,
        is_published boolean NOT NULL,
        nav_key character varying(256),
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_training_programs PRIMARY KEY (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE hr_request_types (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        code character varying(256) NOT NULL,
        name character varying(256) NOT NULL,
        description text,
        form_schema_json text,
        workflow_definition_id uuid,
        nav_operation character varying(256),
        is_sensitive boolean NOT NULL,
        sla_hours integer,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_hr_request_types PRIMARY KEY (id),
        CONSTRAINT fk_hr_request_types_approval_workflows_workflow_definition_id FOREIGN KEY (workflow_definition_id) REFERENCES approval_workflows (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE leave_types (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        nav_key character varying(256) NOT NULL,
        name character varying(256) NOT NULL,
        counts_working_days_only boolean NOT NULL,
        requires_attachment boolean NOT NULL,
        max_days_per_application integer,
        min_notice_days integer NOT NULL,
        allow_negative_balance boolean NOT NULL,
        allow_half_day boolean NOT NULL,
        eligible_gender character varying(256),
        is_active boolean NOT NULL,
        workflow_definition_id uuid,
        last_synced_at_utc timestamp with time zone NOT NULL,
        nav_version_token character varying(256),
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_leave_types PRIMARY KEY (id),
        CONSTRAINT fk_leave_types_approval_workflows_workflow_definition_id FOREIGN KEY (workflow_definition_id) REFERENCES approval_workflows (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE workflow_instances (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        workflow_definition_id uuid NOT NULL,
        definition_version integer NOT NULL,
        document_type character varying(256) NOT NULL,
        document_id uuid NOT NULL,
        document_reference character varying(256) NOT NULL,
        requester_user_id uuid NOT NULL,
        requester_employee_id uuid NOT NULL,
        amount numeric(18,2),
        current_sequence integer NOT NULL,
        status integer NOT NULL,
        completed_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_workflow_instances PRIMARY KEY (id),
        CONSTRAINT fk_workflow_instances_approval_workflows_workflow_definition_id FOREIGN KEY (workflow_definition_id) REFERENCES approval_workflows (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE workflow_steps (
        id uuid NOT NULL,
        workflow_definition_id uuid NOT NULL,
        sequence integer NOT NULL,
        name character varying(256) NOT NULL,
        approver_type integer NOT NULL,
        approver_role character varying(256),
        approver_user_id uuid,
        mode integer NOT NULL,
        min_amount numeric(18,2),
        max_amount numeric(18,2),
        deadline_hours integer,
        reminder_every_hours integer,
        escalate_to_role character varying(256),
        skip_if_approver_is_requester boolean NOT NULL,
        CONSTRAINT pk_workflow_steps PRIMARY KEY (id),
        CONSTRAINT fk_workflow_steps_approval_workflows_workflow_definition_id FOREIGN KEY (workflow_definition_id) REFERENCES approval_workflows (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE companies (
        id uuid NOT NULL,
        code character varying(256) NOT NULL,
        display_name character varying(256) NOT NULL,
        nav_company_name character varying(256) NOT NULL,
        nav_company_guid uuid,
        integration_settings_id uuid,
        is_active boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_companies PRIMARY KEY (id),
        CONSTRAINT fk_companies_nav_integration_settings_integration_settings_id FOREIGN KEY (integration_settings_id) REFERENCES nav_integration_settings (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE nav_endpoint_mappings (
        id uuid NOT NULL,
        integration_settings_id uuid NOT NULL,
        logical_name character varying(256) NOT NULL,
        protocol character varying(256) NOT NULL,
        service_name character varying(256) NOT NULL,
        method character varying(256),
        field_map_json text,
        modified_field character varying(256),
        is_enabled boolean NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_nav_endpoint_mappings PRIMARY KEY (id),
        CONSTRAINT fk_nav_endpoint_mappings_nav_integration_settings_integration_ FOREIGN KEY (integration_settings_id) REFERENCES nav_integration_settings (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE role_permissions (
        role_id uuid NOT NULL,
        permission_id uuid NOT NULL,
        CONSTRAINT pk_role_permissions PRIMARY KEY (role_id, permission_id),
        CONSTRAINT fk_role_permissions_permissions_permission_id FOREIGN KEY (permission_id) REFERENCES permissions (id) ON DELETE CASCADE,
        CONSTRAINT fk_role_permissions_roles_role_id FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE approval_history (
        id uuid NOT NULL,
        workflow_instance_id uuid NOT NULL,
        approval_task_id uuid,
        action integer NOT NULL,
        actor_user_id uuid NOT NULL,
        on_behalf_of_user_id uuid,
        comment text,
        occurred_at_utc timestamp with time zone NOT NULL,
        ip_address character varying(256),
        CONSTRAINT pk_approval_history PRIMARY KEY (id),
        CONSTRAINT fk_approval_history_workflow_instances_workflow_instance_id FOREIGN KEY (workflow_instance_id) REFERENCES workflow_instances (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE departments (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        nav_key character varying(256) NOT NULL,
        name character varying(256) NOT NULL,
        parent_department_id uuid,
        head_employee_id uuid,
        last_synced_at_utc timestamp with time zone NOT NULL,
        nav_version_token character varying(256),
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_departments PRIMARY KEY (id),
        CONSTRAINT fk_departments_companies_company_id FOREIGN KEY (company_id) REFERENCES companies (id) ON DELETE CASCADE,
        CONSTRAINT fk_departments_departments_parent_department_id FOREIGN KEY (parent_department_id) REFERENCES departments (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE users (
        id uuid NOT NULL,
        user_name character varying(256) NOT NULL,
        normalized_user_name character varying(256) NOT NULL,
        email character varying(256) NOT NULL,
        normalized_email character varying(256) NOT NULL,
        password_hash character varying(256),
        email_confirmed boolean NOT NULL,
        phone_number character varying(256),
        mfa_enabled boolean NOT NULL,
        mfa_secret_encrypted text,
        access_failed_count integer NOT NULL,
        lockout_end_utc timestamp with time zone,
        security_stamp character varying(256) NOT NULL,
        is_active boolean NOT NULL,
        external_subject_id character varying(256),
        external_provider character varying(256),
        last_login_at_utc timestamp with time zone,
        default_company_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_users PRIMARY KEY (id),
        CONSTRAINT fk_users_companies_default_company_id FOREIGN KEY (default_company_id) REFERENCES companies (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE employees (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        nav_key character varying(256) NOT NULL,
        first_name character varying(256) NOT NULL,
        middle_name character varying(256),
        last_name character varying(256) NOT NULL,
        work_email character varying(256),
        mobile_phone character varying(256),
        department_id uuid,
        position_id uuid,
        location_id uuid,
        manager_employee_id uuid,
        status integer NOT NULL,
        employment_date date,
        contract_end_date date,
        birth_date date,
        photo_document_id uuid,
        last_synced_at_utc timestamp with time zone NOT NULL,
        nav_version_token character varying(256),
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_employees PRIMARY KEY (id),
        CONSTRAINT fk_employees_companies_company_id FOREIGN KEY (company_id) REFERENCES companies (id) ON DELETE RESTRICT,
        CONSTRAINT fk_employees_departments_department_id FOREIGN KEY (department_id) REFERENCES departments (id) ON DELETE SET NULL,
        CONSTRAINT fk_employees_employees_manager_employee_id FOREIGN KEY (manager_employee_id) REFERENCES employees (id) ON DELETE SET NULL,
        CONSTRAINT fk_employees_locations_location_id FOREIGN KEY (location_id) REFERENCES locations (id) ON DELETE SET NULL,
        CONSTRAINT fk_employees_positions_position_id FOREIGN KEY (position_id) REFERENCES positions (id) ON DELETE SET NULL
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE approval_tasks (
        id uuid NOT NULL,
        workflow_instance_id uuid NOT NULL,
        workflow_step_id uuid NOT NULL,
        sequence integer NOT NULL,
        assignee_user_id uuid NOT NULL,
        on_behalf_of_user_id uuid,
        status integer NOT NULL,
        due_at_utc timestamp with time zone,
        last_reminder_at_utc timestamp with time zone,
        acted_at_utc timestamp with time zone,
        comment text,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_approval_tasks PRIMARY KEY (id),
        CONSTRAINT fk_approval_tasks_users_assignee_user_id FOREIGN KEY (assignee_user_id) REFERENCES users (id) ON DELETE RESTRICT,
        CONSTRAINT fk_approval_tasks_workflow_instances_workflow_instance_id FOREIGN KEY (workflow_instance_id) REFERENCES workflow_instances (id) ON DELETE CASCADE,
        CONSTRAINT fk_approval_tasks_workflow_steps_workflow_step_id FOREIGN KEY (workflow_step_id) REFERENCES workflow_steps (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE notifications (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        recipient_user_id uuid NOT NULL,
        event_code character varying(256) NOT NULL,
        channel integer NOT NULL,
        subject character varying(256),
        body text NOT NULL,
        link_url character varying(1024),
        status integer NOT NULL,
        attempts integer NOT NULL,
        next_attempt_at_utc timestamp with time zone,
        sent_at_utc timestamp with time zone,
        read_at_utc timestamp with time zone,
        provider_message_id character varying(256),
        last_error text,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_notifications PRIMARY KEY (id),
        CONSTRAINT fk_notifications_users_recipient_user_id FOREIGN KEY (recipient_user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE refresh_tokens (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        token_hash character varying(256) NOT NULL,
        family_id character varying(256) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        revoked_at_utc timestamp with time zone,
        replaced_by_hash character varying(256),
        created_by_ip character varying(256),
        user_agent character varying(1024),
        CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
        CONSTRAINT fk_refresh_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE user_roles (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        role_id uuid NOT NULL,
        company_id uuid,
        scope_department_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_user_roles PRIMARY KEY (id),
        CONSTRAINT fk_user_roles_companies_company_id FOREIGN KEY (company_id) REFERENCES companies (id),
        CONSTRAINT fk_user_roles_departments_scope_department_id FOREIGN KEY (scope_department_id) REFERENCES departments (id),
        CONSTRAINT fk_user_roles_roles_role_id FOREIGN KEY (role_id) REFERENCES roles (id) ON DELETE CASCADE,
        CONSTRAINT fk_user_roles_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE attendance_records (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        employee_id uuid NOT NULL,
        work_date date NOT NULL,
        clock_in_utc timestamp with time zone,
        clock_out_utc timestamp with time zone,
        source integer NOT NULL,
        source_device_id character varying(256),
        external_ref character varying(256),
        late_minutes integer NOT NULL,
        worked_hours numeric(6,2) NOT NULL,
        shift_code character varying(256),
        latitude numeric(9,6),
        longitude numeric(9,6),
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_attendance_records PRIMARY KEY (id),
        CONSTRAINT fk_attendance_records_employees_employee_id FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE attendance_requests (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        reference character varying(256) NOT NULL,
        employee_id uuid NOT NULL,
        type integer NOT NULL,
        work_date date NOT NULL,
        requested_clock_in_utc timestamp with time zone,
        requested_clock_out_utc timestamp with time zone,
        overtime_hours numeric(6,2),
        reason text NOT NULL,
        status integer NOT NULL,
        workflow_instance_id uuid,
        nav_status integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_attendance_requests PRIMARY KEY (id),
        CONSTRAINT fk_attendance_requests_employees_employee_id FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE employee_documents (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        employee_id uuid,
        category_id uuid NOT NULL,
        title character varying(256) NOT NULL,
        visibility integer NOT NULL,
        department_id uuid,
        expiry_date date,
        current_version integer NOT NULL,
        owner_entity_type character varying(256),
        owner_entity_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_employee_documents PRIMARY KEY (id),
        CONSTRAINT fk_employee_documents_document_categories_category_id FOREIGN KEY (category_id) REFERENCES document_categories (id) ON DELETE RESTRICT,
        CONSTRAINT fk_employee_documents_employees_employee_id FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE employee_mappings (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        user_id uuid NOT NULL,
        employee_id uuid NOT NULL,
        nav_employee_no character varying(256) NOT NULL,
        is_primary boolean NOT NULL,
        verified_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_employee_mappings PRIMARY KEY (id),
        CONSTRAINT fk_employee_mappings_companies_company_id FOREIGN KEY (company_id) REFERENCES companies (id) ON DELETE CASCADE,
        CONSTRAINT fk_employee_mappings_employees_employee_id FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE CASCADE,
        CONSTRAINT fk_employee_mappings_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE employee_training (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        reference character varying(256) NOT NULL,
        employee_id uuid NOT NULL,
        training_program_id uuid NOT NULL,
        status integer NOT NULL,
        workflow_instance_id uuid,
        feedback_rating integer,
        feedback_comment character varying(256),
        certificate_document_id uuid,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_employee_training PRIMARY KEY (id),
        CONSTRAINT fk_employee_training_employees_employee_id FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE RESTRICT,
        CONSTRAINT fk_employee_training_training_programs_training_program_id FOREIGN KEY (training_program_id) REFERENCES training_programs (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE expense_claims (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        reference character varying(256) NOT NULL,
        employee_id uuid NOT NULL,
        purpose character varying(256) NOT NULL,
        currency character varying(256) NOT NULL,
        total_amount numeric(18,2) NOT NULL,
        project_code character varying(256),
        cost_centre_code character varying(256),
        status integer NOT NULL,
        workflow_instance_id uuid,
        nav_status integer NOT NULL,
        nav_document_no character varying(256),
        payment_reference character varying(256),
        paid_at_utc timestamp with time zone,
        submitted_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_expense_claims PRIMARY KEY (id),
        CONSTRAINT fk_expense_claims_employees_employee_id FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE hr_requests (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        reference character varying(256) NOT NULL,
        employee_id uuid NOT NULL,
        request_type_id uuid NOT NULL,
        subject character varying(256) NOT NULL,
        payload_json text,
        payload_encrypted boolean NOT NULL,
        status integer NOT NULL,
        workflow_instance_id uuid,
        nav_status integer NOT NULL,
        assigned_to_user_id uuid,
        submitted_at_utc timestamp with time zone,
        resolved_at_utc timestamp with time zone,
        resolution text,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_hr_requests PRIMARY KEY (id),
        CONSTRAINT fk_hr_requests_employees_employee_id FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE RESTRICT,
        CONSTRAINT fk_hr_requests_hr_request_types_request_type_id FOREIGN KEY (request_type_id) REFERENCES hr_request_types (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE leave_applications (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        reference character varying(256) NOT NULL,
        employee_id uuid NOT NULL,
        leave_type_id uuid NOT NULL,
        start_date date NOT NULL,
        end_date date NOT NULL,
        start_half_day boolean NOT NULL,
        end_half_day boolean NOT NULL,
        days_requested numeric(9,2) NOT NULL,
        reason text,
        reliever_employee_id uuid,
        status integer NOT NULL,
        workflow_instance_id uuid,
        nav_status integer NOT NULL,
        nav_document_no character varying(256),
        submitted_at_utc timestamp with time zone,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_leave_applications PRIMARY KEY (id),
        CONSTRAINT fk_leave_applications_employees_employee_id FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE RESTRICT,
        CONSTRAINT fk_leave_applications_employees_reliever_employee_id FOREIGN KEY (reliever_employee_id) REFERENCES employees (id) ON DELETE SET NULL,
        CONSTRAINT fk_leave_applications_leave_types_leave_type_id FOREIGN KEY (leave_type_id) REFERENCES leave_types (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE leave_balances_cache (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        employee_id uuid NOT NULL,
        leave_type_id uuid NOT NULL,
        leave_period_code character varying(256) NOT NULL,
        entitlement numeric(9,2) NOT NULL,
        brought_forward numeric(9,2) NOT NULL,
        taken numeric(9,2) NOT NULL,
        pending_approval numeric(9,2) NOT NULL,
        available numeric(9,2) NOT NULL,
        retrieved_at_utc timestamp with time zone NOT NULL,
        CONSTRAINT pk_leave_balances_cache PRIMARY KEY (id),
        CONSTRAINT fk_leave_balances_cache_employees_employee_id FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE CASCADE,
        CONSTRAINT fk_leave_balances_cache_leave_types_leave_type_id FOREIGN KEY (leave_type_id) REFERENCES leave_types (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE payslip_metadata (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        employee_id uuid NOT NULL,
        payroll_period_id uuid NOT NULL,
        nav_employee_no character varying(256) NOT NULL,
        nav_period_key character varying(256) NOT NULL,
        net_pay numeric(18,2),
        currency character varying(256) NOT NULL,
        snapshot_encrypted text,
        pdf_storage_key character varying(256),
        pdf_sha256 character varying(256),
        retrieved_at_utc timestamp with time zone NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_payslip_metadata PRIMARY KEY (id),
        CONSTRAINT fk_payslip_metadata_employees_employee_id FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE RESTRICT,
        CONSTRAINT fk_payslip_metadata_payroll_periods_cache_payroll_period_id FOREIGN KEY (payroll_period_id) REFERENCES payroll_periods_cache (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE profile_change_requests (
        id uuid NOT NULL,
        company_id uuid NOT NULL,
        reference character varying(256) NOT NULL,
        employee_id uuid NOT NULL,
        section character varying(256) NOT NULL,
        changes_json text NOT NULL,
        changes_encrypted boolean NOT NULL,
        status integer NOT NULL,
        workflow_instance_id uuid,
        nav_status integer NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        created_by character varying(256),
        modified_at_utc timestamp with time zone,
        modified_by character varying(256),
        is_deleted boolean NOT NULL,
        deleted_at_utc timestamp with time zone,
        deleted_by character varying(256),
        CONSTRAINT pk_profile_change_requests PRIMARY KEY (id),
        CONSTRAINT fk_profile_change_requests_employees_employee_id FOREIGN KEY (employee_id) REFERENCES employees (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE document_versions (
        id uuid NOT NULL,
        document_id uuid NOT NULL,
        version integer NOT NULL,
        file_name character varying(256) NOT NULL,
        content_type character varying(256) NOT NULL,
        size_bytes bigint NOT NULL,
        storage_key character varying(1024) NOT NULL,
        sha256 character varying(256) NOT NULL,
        uploaded_at_utc timestamp with time zone NOT NULL,
        uploaded_by character varying(256) NOT NULL,
        malware_scan_passed boolean NOT NULL,
        CONSTRAINT pk_document_versions PRIMARY KEY (id),
        CONSTRAINT fk_document_versions_employee_documents_document_id FOREIGN KEY (document_id) REFERENCES employee_documents (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE TABLE expense_claim_lines (
        id uuid NOT NULL,
        expense_claim_id uuid NOT NULL,
        line_no integer NOT NULL,
        category_id uuid NOT NULL,
        expense_date date NOT NULL,
        description text NOT NULL,
        amount numeric(18,2) NOT NULL,
        currency character varying(256) NOT NULL,
        exchange_rate numeric(18,6) NOT NULL,
        receipt_document_id uuid,
        CONSTRAINT pk_expense_claim_lines PRIMARY KEY (id),
        CONSTRAINT fk_expense_claim_lines_employee_documents_receipt_document_id FOREIGN KEY (receipt_document_id) REFERENCES employee_documents (id) ON DELETE SET NULL,
        CONSTRAINT fk_expense_claim_lines_expense_categories_category_id FOREIGN KEY (category_id) REFERENCES expense_categories (id) ON DELETE RESTRICT,
        CONSTRAINT fk_expense_claim_lines_expense_claims_expense_claim_id FOREIGN KEY (expense_claim_id) REFERENCES expense_claims (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_announcements_company_id_publish_at_utc ON announcements (company_id, publish_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_approval_delegations_from_user_id_starts_at_utc_ends_at_utc ON approval_delegations (from_user_id, starts_at_utc, ends_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_approval_history_workflow_instance_id_occurred_at_utc ON approval_history (workflow_instance_id, occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_approval_tasks_assignee_user_id_status ON approval_tasks (assignee_user_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_approval_tasks_status_due_at_utc ON approval_tasks (status, due_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_approval_tasks_workflow_instance_id ON approval_tasks (workflow_instance_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_approval_tasks_workflow_step_id ON approval_tasks (workflow_step_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_approval_workflows_company_id_code ON approval_workflows (company_id, code) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_approval_workflows_company_id_document_type_is_active ON approval_workflows (company_id, document_type, is_active);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_attendance_records_employee_id_work_date ON attendance_records (employee_id, work_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_attendance_records_source_external_ref ON attendance_records (source, external_ref) WHERE external_ref IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_attendance_requests_company_id_reference ON attendance_requests (company_id, reference);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_attendance_requests_employee_id ON attendance_requests (employee_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_audit_logs_action ON audit_logs (action);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_audit_logs_entity_type_entity_id ON audit_logs (entity_type, entity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_audit_logs_occurred_at_utc ON audit_logs (occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_audit_logs_user_id_occurred_at_utc ON audit_logs (user_id, occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_companies_code ON companies (code) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_companies_integration_settings_id ON companies (integration_settings_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_departments_company_id_nav_key ON departments (company_id, nav_key) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_departments_parent_department_id ON departments (parent_department_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_document_access_logs_document_id_accessed_at_utc ON document_access_logs (document_id, accessed_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_document_categories_company_id_code ON document_categories (company_id, code) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_document_versions_document_id_version ON document_versions (document_id, version);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employee_documents_category_id ON employee_documents (category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employee_documents_company_id_employee_id_category_id ON employee_documents (company_id, employee_id, category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employee_documents_employee_id ON employee_documents (employee_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employee_documents_expiry_date ON employee_documents (expiry_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employee_documents_owner_entity_type_owner_entity_id ON employee_documents (owner_entity_type, owner_entity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_employee_mappings_company_id_nav_employee_no ON employee_mappings (company_id, nav_employee_no) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employee_mappings_employee_id ON employee_mappings (employee_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_employee_mappings_user_id_company_id ON employee_mappings (user_id, company_id) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_employee_qualifications_employee_id_nav_key ON employee_qualifications (employee_id, nav_key) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employee_qualifications_expires_on ON employee_qualifications (expires_on);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_employee_training_company_id_reference ON employee_training (company_id, reference);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_employee_training_employee_id_training_program_id ON employee_training (employee_id, training_program_id) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employee_training_training_program_id ON employee_training (training_program_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employees_company_id_department_id_status ON employees (company_id, department_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employees_company_id_last_name_first_name ON employees (company_id, last_name, first_name);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_employees_company_id_nav_key ON employees (company_id, nav_key) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employees_department_id ON employees (department_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employees_location_id ON employees (location_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employees_manager_employee_id ON employees (manager_employee_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_employees_position_id ON employees (position_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_expense_categories_company_id_code ON expense_categories (company_id, code) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_expense_claim_lines_category_id ON expense_claim_lines (category_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_expense_claim_lines_expense_claim_id_line_no ON expense_claim_lines (expense_claim_id, line_no);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_expense_claim_lines_receipt_document_id ON expense_claim_lines (receipt_document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_expense_claims_company_id_reference ON expense_claims (company_id, reference);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_expense_claims_employee_id_status ON expense_claims (employee_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_expense_claims_nav_status ON expense_claims (nav_status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_hr_request_types_company_id_code ON hr_request_types (company_id, code) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_hr_request_types_workflow_definition_id ON hr_request_types (workflow_definition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_hr_requests_assigned_to_user_id_status ON hr_requests (assigned_to_user_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_hr_requests_company_id_reference ON hr_requests (company_id, reference);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_hr_requests_employee_id_status ON hr_requests (employee_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_hr_requests_request_type_id ON hr_requests (request_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_integration_queue_company_id_operation_idempotency_key ON integration_queue (company_id, operation, idempotency_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_integration_queue_entity_type_entity_id ON integration_queue (entity_type, entity_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_integration_queue_status_next_attempt_at_utc_priority ON integration_queue (status, next_attempt_at_utc, priority);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_leave_applications_company_id_reference ON leave_applications (company_id, reference);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_leave_applications_company_id_status ON leave_applications (company_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_leave_applications_employee_id_start_date_end_date ON leave_applications (employee_id, start_date, end_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_leave_applications_leave_type_id ON leave_applications (leave_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_leave_applications_nav_status ON leave_applications (nav_status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_leave_applications_reliever_employee_id ON leave_applications (reliever_employee_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_leave_balances_cache_employee_id_leave_type_id_leave_period ON leave_balances_cache (employee_id, leave_type_id, leave_period_code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_leave_balances_cache_leave_type_id ON leave_balances_cache (leave_type_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_leave_types_company_id_nav_key ON leave_types (company_id, nav_key) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_leave_types_workflow_definition_id ON leave_types (workflow_definition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_locations_company_id_nav_key ON locations (company_id, nav_key) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_nav_endpoint_mappings_integration_settings_id_logical_name ON nav_endpoint_mappings (integration_settings_id, logical_name) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_nav_integration_settings_name ON nav_integration_settings (name) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_nav_sync_jobs_company_id_entity_name_started_at_utc ON nav_sync_jobs (company_id, entity_name, started_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_nav_sync_jobs_status ON nav_sync_jobs (status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_nav_sync_logs_correlation_id ON nav_sync_logs (correlation_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_nav_sync_logs_occurred_at_utc ON nav_sync_logs (occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_nav_sync_logs_queue_item_id ON nav_sync_logs (queue_item_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_nav_sync_logs_success_occurred_at_utc ON nav_sync_logs (success, occurred_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_nav_sync_logs_sync_job_id ON nav_sync_logs (sync_job_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_notification_templates_company_id_event_code_channel ON notification_templates (company_id, event_code, channel) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_notifications_recipient_user_id_channel_read_at_utc ON notifications (recipient_user_id, channel, read_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_notifications_status_next_attempt_at_utc ON notifications (status, next_attempt_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_payroll_periods_cache_company_id_nav_key ON payroll_periods_cache (company_id, nav_key);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_payroll_periods_cache_company_id_start_date ON payroll_periods_cache (company_id, start_date);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_payslip_metadata_employee_id_payroll_period_id ON payslip_metadata (employee_id, payroll_period_id) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_payslip_metadata_payroll_period_id ON payslip_metadata (payroll_period_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_permissions_code ON permissions (code);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_positions_company_id_nav_key ON positions (company_id, nav_key) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_profile_change_requests_company_id_reference ON profile_change_requests (company_id, reference);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_profile_change_requests_employee_id_status ON profile_change_requests (employee_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_public_holidays_company_id_date ON public_holidays (company_id, date) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_refresh_tokens_family_id ON refresh_tokens (family_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_refresh_tokens_token_hash ON refresh_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_refresh_tokens_user_id_expires_at_utc ON refresh_tokens (user_id, expires_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_role_permissions_permission_id ON role_permissions (permission_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_roles_normalized_name ON roles (normalized_name) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_shifts_company_id_code ON shifts (company_id, code) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_system_settings_company_id_key ON system_settings (company_id, key) NULLS NOT DISTINCT WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_training_programs_company_id_code ON training_programs (company_id, code) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_user_roles_company_id ON user_roles (company_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_user_roles_role_id ON user_roles (role_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_user_roles_scope_department_id ON user_roles (scope_department_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_user_roles_user_id_role_id_company_id_scope_department_id ON user_roles (user_id, role_id, company_id, scope_department_id) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_users_default_company_id ON users (default_company_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_users_external_provider_external_subject_id ON users (external_provider, external_subject_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_users_normalized_email ON users (normalized_email) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE UNIQUE INDEX ix_users_normalized_user_name ON users (normalized_user_name) WHERE NOT is_deleted;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_workflow_instances_document_type_document_id ON workflow_instances (document_type, document_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_workflow_instances_requester_user_id_status ON workflow_instances (requester_user_id, status);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_workflow_instances_workflow_definition_id ON workflow_instances (workflow_definition_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    CREATE INDEX ix_workflow_steps_workflow_definition_id_sequence ON workflow_steps (workflow_definition_id, sequence);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126133332_InitialSchema') THEN
    INSERT INTO __ef_migrations_history (migration_id, product_version)
    VALUES ('20261126133332_InitialSchema', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126135545_Phase2Identity') THEN
    ALTER TABLE users ADD mfa_last_time_step bigint;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126135545_Phase2Identity') THEN
    ALTER TABLE users ADD mfa_recovery_code_hashes text;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126135545_Phase2Identity') THEN
    ALTER TABLE users ADD must_change_password boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126135545_Phase2Identity') THEN
    ALTER TABLE users ADD password_changed_at_utc timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126135545_Phase2Identity') THEN
    CREATE TABLE password_reset_tokens (
        id uuid NOT NULL,
        user_id uuid NOT NULL,
        token_hash character varying(256) NOT NULL,
        created_at_utc timestamp with time zone NOT NULL,
        expires_at_utc timestamp with time zone NOT NULL,
        used_at_utc timestamp with time zone,
        purpose character varying(256) NOT NULL,
        requested_from_ip character varying(256),
        CONSTRAINT pk_password_reset_tokens PRIMARY KEY (id),
        CONSTRAINT fk_password_reset_tokens_users_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126135545_Phase2Identity') THEN
    CREATE UNIQUE INDEX ix_password_reset_tokens_token_hash ON password_reset_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126135545_Phase2Identity') THEN
    CREATE INDEX ix_password_reset_tokens_user_id_created_at_utc ON password_reset_tokens (user_id, created_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126135545_Phase2Identity') THEN
    INSERT INTO __ef_migrations_history (migration_id, product_version)
    VALUES ('20261126135545_Phase2Identity', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126141152_Phase3Workflow') THEN
    ALTER TABLE workflow_instances ADD link_url character varying(1024);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126141152_Phase3Workflow') THEN
    ALTER TABLE workflow_instances ADD summary character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126141152_Phase3Workflow') THEN
    ALTER TABLE approval_tasks ADD escalated_at_utc timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126141152_Phase3Workflow') THEN
    ALTER TABLE approval_tasks ADD escalated_from_task_id uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261126141152_Phase3Workflow') THEN
    INSERT INTO __ef_migrations_history (migration_id, product_version)
    VALUES ('20261126141152_Phase3Workflow', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261127071610_Phase5Administration') THEN
    ALTER TABLE employees ADD gender character varying(256);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261127071610_Phase5Administration') THEN
    ALTER TABLE employees ADD termination_date date;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261127071610_Phase5Administration') THEN
    ALTER TABLE announcements ADD notified_at_utc timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261127071610_Phase5Administration') THEN
    ALTER TABLE announcements ADD notify_by_email boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261127071610_Phase5Administration') THEN
    ALTER TABLE announcements ADD notify_on_publish boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261127071610_Phase5Administration') THEN
    -- Read-only reporting views for Power BI and other BI tools (spec §13).
    -- They expose only what reports need: no birth dates, contact details, bank data, pay, request contents or free-text reasons.
    -- Grant BI accounts access to this schema only (see deploy/sql/reporting-role.sql), never to the application tables.
    CREATE SCHEMA IF NOT EXISTS reporting;

    CREATE OR REPLACE VIEW reporting.employees AS
    SELECT e.id AS employee_id, e.company_id, e.nav_key AS employee_no, e.department_id, d.name AS department, e.position_id, p.title AS position,
           e.location_id, l.name AS location, e.manager_employee_id,
           CASE e.status WHEN 0 THEN 'Active' WHEN 1 THEN 'Inactive' WHEN 2 THEN 'Terminated' WHEN 3 THEN 'On probation' WHEN 4 THEN 'Suspended' END AS employment_status,
           e.employment_date, e.contract_end_date, e.termination_date, e.gender, e.last_synced_at_utc
    FROM employees e
    LEFT JOIN departments d ON d.id = e.department_id
    LEFT JOIN positions p ON p.id = e.position_id
    LEFT JOIN locations l ON l.id = e.location_id
    WHERE NOT e.is_deleted;

    CREATE OR REPLACE VIEW reporting.departments AS
    SELECT id AS department_id, company_id, nav_key AS code, name, parent_department_id, head_employee_id
    FROM departments WHERE NOT is_deleted;

    CREATE OR REPLACE VIEW reporting.leave_applications AS
    SELECT a.id AS leave_application_id, a.company_id, a.reference, a.employee_id, t.nav_key AS leave_type_code, t.name AS leave_type,
           a.start_date, a.end_date, a.days_requested,
           CASE a.status WHEN 0 THEN 'Draft' WHEN 1 THEN 'Submitted' WHEN 2 THEN 'In approval' WHEN 3 THEN 'Returned' WHEN 4 THEN 'Approved'
                         WHEN 5 THEN 'Rejected' WHEN 6 THEN 'Cancelled' WHEN 7 THEN 'Withdrawn' WHEN 8 THEN 'Completed' END AS status,
           CASE a.nav_status WHEN 0 THEN 'Not required' WHEN 1 THEN 'Pending' WHEN 2 THEN 'In progress' WHEN 3 THEN 'Posted' WHEN 4 THEN 'Failed' WHEN 5 THEN 'Reconciling' END AS nav_status,
           a.nav_document_no, a.submitted_at_utc
    FROM leave_applications a
    JOIN leave_types t ON t.id = a.leave_type_id
    WHERE NOT a.is_deleted AND a.status <> 0;

    CREATE OR REPLACE VIEW reporting.leave_balances AS
    SELECT b.company_id, b.employee_id, t.nav_key AS leave_type_code, t.name AS leave_type, b.leave_period_code,
           b.entitlement, b.brought_forward, b.taken, b.pending_approval, b.available, b.retrieved_at_utc
    FROM leave_balances_cache b
    JOIN leave_types t ON t.id = b.leave_type_id;

    CREATE OR REPLACE VIEW reporting.expense_claims AS
    SELECT c.id AS expense_claim_id, c.company_id, c.reference, c.employee_id, c.currency, c.total_amount, c.project_code, c.cost_centre_code,
           CASE c.status WHEN 0 THEN 'Draft' WHEN 1 THEN 'Submitted' WHEN 2 THEN 'In approval' WHEN 3 THEN 'Returned' WHEN 4 THEN 'Approved'
                         WHEN 5 THEN 'Rejected' WHEN 6 THEN 'Cancelled' WHEN 7 THEN 'Withdrawn' WHEN 8 THEN 'Completed' END AS status,
           CASE c.nav_status WHEN 0 THEN 'Not required' WHEN 1 THEN 'Pending' WHEN 2 THEN 'In progress' WHEN 3 THEN 'Posted' WHEN 4 THEN 'Failed' WHEN 5 THEN 'Reconciling' END AS nav_status,
           c.nav_document_no, c.submitted_at_utc, c.paid_at_utc
    FROM expense_claims c
    WHERE NOT c.is_deleted AND c.status <> 0;

    CREATE OR REPLACE VIEW reporting.hr_requests AS
    SELECT r.id AS hr_request_id, r.company_id, r.reference, r.employee_id, t.code AS request_type_code, t.name AS request_type, t.sla_hours,
           CASE r.status WHEN 0 THEN 'Draft' WHEN 1 THEN 'Submitted' WHEN 2 THEN 'In approval' WHEN 3 THEN 'Returned' WHEN 4 THEN 'Approved'
                         WHEN 5 THEN 'Rejected' WHEN 6 THEN 'Cancelled' WHEN 7 THEN 'Withdrawn' WHEN 8 THEN 'Completed' END AS status,
           r.submitted_at_utc, r.resolved_at_utc
    FROM hr_requests r
    JOIN hr_request_types t ON t.id = r.request_type_id
    WHERE NOT r.is_deleted;

    CREATE OR REPLACE VIEW reporting.training_participation AS
    SELECT et.id AS participation_id, et.company_id, et.employee_id, p.code AS programme_code, p.title AS programme, p.provider, p.starts_at_utc, p.ends_at_utc,
           CASE et.status WHEN 0 THEN 'Applied' WHEN 1 THEN 'Approved' WHEN 2 THEN 'Rejected' WHEN 3 THEN 'Registered' WHEN 4 THEN 'Attended'
                          WHEN 5 THEN 'Completed' WHEN 6 THEN 'No show' WHEN 7 THEN 'Cancelled' END AS status,
           et.feedback_rating, p.cost_per_participant
    FROM employee_training et
    JOIN training_programs p ON p.id = et.training_program_id
    WHERE NOT et.is_deleted;

    CREATE OR REPLACE VIEW reporting.attendance_daily AS
    SELECT a.company_id, a.employee_id, a.work_date, a.clock_in_utc, a.clock_out_utc, a.worked_hours, a.late_minutes,
           CASE a.source WHEN 0 THEN 'Portal' WHEN 1 THEN 'Biometric' WHEN 2 THEN 'NAV' WHEN 3 THEN 'Import' END AS source
    FROM attendance_records a
    WHERE NOT a.is_deleted;

    CREATE OR REPLACE VIEW reporting.approvals AS
    SELECT t.id AS approval_task_id, i.company_id, i.document_type, i.document_reference, i.requester_employee_id, s.name AS step, t.sequence,
           CASE t.status WHEN 0 THEN 'Pending' WHEN 1 THEN 'Approved' WHEN 2 THEN 'Rejected' WHEN 3 THEN 'Returned' WHEN 4 THEN 'Skipped' WHEN 5 THEN 'Superseded' END AS status,
           t.created_at_utc AS assigned_at_utc, t.due_at_utc, t.acted_at_utc, (t.escalated_from_task_id IS NOT NULL) AS escalated
    FROM approval_tasks t
    JOIN workflow_instances i ON i.id = t.workflow_instance_id
    JOIN workflow_steps s ON s.id = t.workflow_step_id
    WHERE NOT t.is_deleted;

    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261127071610_Phase5Administration') THEN
    INSERT INTO __ef_migrations_history (migration_id, product_version)
    VALUES ('20261127071610_Phase5Administration', '8.0.11');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;


DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261127083636_Phase6Performance') THEN
    CREATE INDEX ix_leave_applications_company_id_submitted_at_utc ON leave_applications (company_id, submitted_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261127083636_Phase6Performance') THEN
    CREATE INDEX ix_expense_claims_company_id_submitted_at_utc ON expense_claims (company_id, submitted_at_utc);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM __ef_migrations_history WHERE "migration_id" = '20261127083636_Phase6Performance') THEN
    INSERT INTO __ef_migrations_history (migration_id, product_version)
    VALUES ('20261127083636_Phase6Performance', '8.0.11');
    END IF;
END $EF$;
COMMIT;

