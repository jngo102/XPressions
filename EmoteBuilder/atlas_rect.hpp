#ifndef ATLAS_RECT_HPP
#define ATLAS_RECT_HPP

#include <QObject>

class RectSize
{
public:
    int width, height;
};

class Rect
{
public:
    static bool isContainedIn(Rect a, Rect b);
    Rect copy();

    int x, y, width, height;
};

#endif // ATLAS_RECT_HPP
