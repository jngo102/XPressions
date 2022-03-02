#include "atlas_rect.hpp"

bool Rect::isContainedIn(Rect a, Rect b)
{
    return (a.x >= b.x) && (a.y >= b.y)
        && (a.x + a.width <= b.x + b.width)
        && (a.y + a.height <= b.y + b.height);
}

Rect Rect::copy()
{
    Rect r;
    r.x = x;
    r.y = y;
    r.width = width;
    r.height = height;
    return r;
}
