SET ANSI_NULLS              ON;
SET ANSI_PADDING            ON;
SET ANSI_WARNINGS           ON;
SET ANSI_NULL_DFLT_ON       ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER       ON;
go

CREATE PROCEDURE dbo.sp_get_replication_counts
AS
BEGIN
    SET NOCOUNT ON;

    SELECT UPPER(LEFT(ta.name, 1)) + LOWER(SUBSTRING(ta.name, 2, 100)) AS EntityName, SUM(pa.rows) AS [Count]
    FROM sys.tables ta INNER JOIN sys.partitions pa ON pa.OBJECT_ID = ta.OBJECT_ID
                       INNER JOIN sys.schemas sc ON ta.schema_id = sc.schema_id
    WHERE
        sc.name='dbo' AND ta.is_ms_shipped = 0 AND pa.index_id IN (0,1) AND
        ta.name IN ('opportunityproduct', 'territory', 'lead', 'opportunity', 'account', 'systemusermanagermap', /* 'businessunit', */ 'systemuser', 'product')
    GROUP BY ta.name
    ORDER BY ta.name;
END;
go


CREATE PROCEDURE dbo.sp_get_pull_status
AS
BEGIN
    SET NOCOUNT ON;
    
    --InitialPullComplete statuses
    -- -1 -> Initial State
    -- 1 -> Data is present but not complete
    -- 2 -> Data pull is complete
    -- 3 -> No data is present

    DECLARE @StatusCode INT = -1;

    SELECT ta.[name] AS EntityName, SUM(pa.[rows]) AS [Count] INTO #counts
    FROM sys.tables ta INNER JOIN sys.partitions pa ON pa.OBJECT_ID = ta.OBJECT_ID
                       INNER JOIN sys.schemas sc ON ta.schema_id = sc.schema_id
    WHERE
        sc.name='dbo' AND ta.is_ms_shipped = 0 AND pa.index_id IN (0,1) AND
	    ta.name IN ('opportunityproduct', 'territory', 'lead', 'opportunity', 'account', 'systemusermanagermap', /* 'businessunit', */ 'systemuser', 'product')
    GROUP BY ta.[name];

SELECT CASE
                WHEN c.[Count] = 0 AND i.initialcount = 0 THEN 100 
                ELSE (
                        CASE
                            WHEN 100.0*c.[Count] / nullif(i.initialCount,0) IS NULL THEN 0.0
                            ELSE 100.0 * c.[Count]/i.initialCount 
                        END
                     ) 
            END AS [Percentage], 
            c.EntityName as EntityName INTO #percentages
		FROM #counts c INNER JOIN smgt.entityinitialcount i ON i.entityname = c.entityname



	DECLARE @DeploymentTimestamp datetime2;
 	SELECT @DeploymentTimestamp = Convert(DATETIME2, [value], 126)
	FROM smgt.[configuration] WHERE configuration_group = 'SolutionTemplate' AND configuration_subgroup = 'Notifier' AND [name] = 'DeploymentTimestamp';

    IF EXISTS (SELECT *
               FROM #counts
               WHERE [Count] > 0 AND DATEDIFF(HOUR, @DeploymentTimestamp, Sysdatetime()) > 24)
	       SET @StatusCode = 1 --Data pull is partially complete

		
	
    DECLARE @CompletePercentage FLOAT;
    SELECT @CompletePercentage = Convert(float, [value])
    FROM smgt.[configuration] WHERE configuration_group = 'SolutionTemplate' AND configuration_subgroup = 'Notifier' AND [name] = 'DataPullCompleteThreshold';

	IF NOT EXISTS(SELECT p.[Percentage], p.[EntityName], i.lasttimestamp,  DATEDIFF(MINUTE, i.lasttimestamp, Sysdatetime()) AS [TimeDifference] FROM #percentages p
              INNER JOIN smgt.entityinitialcount i ON i.entityName = p.EntityName
              WHERE (p.[Percentage] <= @CompletePercentage OR p.[Percentage] IS NULL) AND DATEDIFF(MINUTE, i.lasttimestamp, Sysdatetime()) > 5
              OR (p.[Percentage] <= @CompletePercentage  OR p.[Percentage] IS NULL))
        SET @StatusCode = 2; --Data pull complete

    DECLARE @EntitiesWithNoData INT, @CountsRows INT;
    SELECT @EntitiesWithNoData = COUNT(*) FROM #counts WHERE [Count] = 0;
    SELECT @CountsRows = COUNT(*) FROM #counts;
    IF @EntitiesWithNoData = @CountsRows AND DATEDIFF(HOUR, @DeploymentTimestamp, Sysdatetime()) > 24
        SET @StatusCode = 3; --No data is present
	
	DECLARE @ASDeployment bit = 0;

    IF EXISTS (SELECT * FROM smgt.[configuration] WHERE configuration_group = 'SolutionTemplate' AND configuration_subgroup = 'Notifier' AND [name] = 'ASDeployment' AND [value] ='true')
	SET @ASDeployment = 1;

    -- AS Flow
    IF @ASDeployment=1 AND DATEDIFF(HOUR, @DeploymentTimestamp, Sysdatetime()) < 24 AND NOT EXISTS (SELECT * FROM smgt.ssas_jobs WHERE [statusMessage] = 'Success')
	SET @StatusCode = -1;

    -- Delayed Processing Flow
    DECLARE @c1 INT, @c2 INT;
    SELECT @c1 = COUNT(*) FROM #counts;
    SELECT @c2 = COUNT(*) from smgt.entityinitialcount;
    IF @c1<>@c2 
    SET @StatusCode = -1;


	UPDATE smgt.[configuration] 
	SET [configuration].[value] = @StatusCode
	WHERE [configuration].configuration_group = 'SolutionTemplate' AND [configuration].configuration_subgroup = 'Notifier' AND [configuration].[name] = 'DataPullStatus'

    MERGE smgt.entityinitialcount AS target
    USING #counts AS source
    ON (target.entityname = source.entityname)
    WHEN MATCHED AND source.[Count] > target.lastcount
    THEN
        UPDATE SET target.lastcount = source.[Count], target.lasttimestamp = Sysdatetime();

END;
GO



CREATE PROCEDURE dbo.sp_get_prior_content
AS
BEGIN
    SET NOCOUNT ON;

    SELECT Count(*) AS ExistingObjectCount
    FROM   information_schema.tables
    WHERE  ( table_schema = 'dbo' AND
             table_name IN ('account', 'businessunit', 'lead', 'opportunity', 'opportunityproduct', 'product', 'systemuser', 'systemusermanagermap', 'territory')
           ) OR
           ( table_schema = 'smgt' AND
             table_name IN ('AccountView', 'ActualSales', 'ActualSalesView', 'BusinessUnitView', 'configuration', 'ConfigurationView', 'date', 'DateView',
                            'LeadView', 'MeasuresView', 'OpportunityProductView', 'OpportunityView', 'ProductView', 'Quotas', 'QuotaView', 'Targets',
                            'TargetView', 'TempUserView', 'TerritoryView', 'UserAscendantsView', 'userMapping', 'UserView'
                           )
           );
END;
go
