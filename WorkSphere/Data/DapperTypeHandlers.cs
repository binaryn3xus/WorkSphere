using System.Data;
using Dapper;

namespace WorkSphere.Data;

public class DateOnlyTypeHandler : SqlMapper.TypeHandler<DateOnly>
{
    public override void SetValue(IDbDataParameter parameter, DateOnly value)
    {
        parameter.DbType = DbType.Date;
        parameter.Value = value;
    }

    public override DateOnly Parse(object value)
    {
        if (value is DateOnly date)
            return date;
        return DateOnly.FromDateTime((DateTime)value);
    }
}

public class TimeOnlyTypeHandler : SqlMapper.TypeHandler<TimeOnly>
{
    public override void SetValue(IDbDataParameter parameter, TimeOnly value)
    {
        parameter.DbType = DbType.Time;
        parameter.Value = value;
    }

    public override TimeOnly Parse(object value)
    {
        if (value is TimeOnly time)
            return time;
        if (value is TimeSpan span)
            return TimeOnly.FromTimeSpan(span);
        return TimeOnly.FromDateTime((DateTime)value);
    }
}

public static class DapperTypeHandlers
{
    public static void Register()
    {
        // Explicitly map the types to DbType to help Dapper's parameter lookup
        SqlMapper.AddTypeMap(typeof(DateOnly), DbType.Date);
        SqlMapper.AddTypeMap(typeof(TimeOnly), DbType.Time);
        
        // Add handlers for the actual value conversion
        SqlMapper.AddTypeHandler(new DateOnlyTypeHandler());
        SqlMapper.AddTypeHandler(new TimeOnlyTypeHandler());
    }
}
