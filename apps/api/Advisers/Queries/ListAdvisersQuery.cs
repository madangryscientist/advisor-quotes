namespace Api.Advisers.Queries;

/// <summary>Every adviser, by name. No parameters, and no <c>X-Adviser-Id</c>: the UI picker needs the list before anyone is chosen.</summary>
public sealed record ListAdvisersQuery;
