# Duplicate QuoteId example

Create a collection:

```bash
curl -i -X POST http://localhost:5000/api/collections \
  -H "Content-Type: application/json" \
  -d '{"name":"Favorite quotes","ownerId":1}'
```

Add quote `42`, then repeat the same request:

```bash
curl -i -X POST http://localhost:5000/api/collections/1/items \
  -H "Content-Type: application/json" \
  -d '{"quoteId":42}'

curl -i -X POST http://localhost:5000/api/collections/1/items \
  -H "Content-Type: application/json" \
  -d '{"quoteId":42}'
```

The repeated request returns:

```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Collection invariant violated",
  "status": 400,
  "detail": "Quote 42 is already in this collection."
}
```
