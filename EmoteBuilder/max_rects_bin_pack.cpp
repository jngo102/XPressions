#include <limits>
#include "atlas_rect.hpp"
#include "max_rects_bin_pack.hpp"

MaxRectsBinPack::MaxRectsBinPack()
{

}

MaxRectsBinPack::MaxRectsBinPack(int width, int height, bool allowRotation)
{
    binWidth = width;
    binHeight = height;
    this->allowRotation = allowRotation;

    Rect n;
    n.x = 0;
    n.y = 0;
    n.width = width;
    n.height = height;

    usedRectangles.clear();

    freeRectangles.clear();
    freeRectangles.append(n);
}

/// Inserts the given list of rectangles in an offline/batch mode, possibly rotated.
/// @param rects The list of rectangles to insert. This vector will be destroyed in the process.
/// @param dst [out] This list will contain the packed rectangles. The indices will not correspond to that of rects.
/// @param method The rectangle placement rule to use when packing.
bool MaxRectsBinPack::insert(QList<RectSize> rects, FreeRectChoiceHeuristic method)
{
    int numRects = rects.count();
    while (rects.count() > 0)
    {
        int bestScore1 = std::numeric_limits<int>::max();
        int bestScore2 = std::numeric_limits<int>::max();
        int bestRectIndex = -1;
        Rect bestNode;

        for (int i = 0; i < rects.count(); ++i)
        {
            int score1 = 0;
            int score2 = 0;
            Rect newNode = scoreRect(rects[i].width, rects[i].height, method, score1, score2);

            if (score1 < bestScore1 || (score1 == bestScore1 && score2 < bestScore2))
            {
                bestScore1 = score1;
                bestScore2 = score2;
                bestNode = newNode;
                bestRectIndex = i;
            }
        }

        if (bestRectIndex == -1)
            return usedRectangles.count() == numRects;

        placeRect(bestNode);
        rects.removeAt(bestRectIndex);
    }

    return usedRectangles.count() == numRects;
}

QList<Rect> MaxRectsBinPack::getMapped()
{
    return usedRectangles;
}


/// Inserts a single rectangle into the bin, possibly rotated.
Rect MaxRectsBinPack::insert(int width, int height, FreeRectChoiceHeuristic method)
{
    Rect newNode;
    int score1 = 0; // Unused in this function. We don't need to know the score after finding the position.
    int score2 = 0;
    switch (method)
    {
        case RectBestShortSideFit: newNode = findPositionForNewNodeBestShortSideFit(width, height, score1, score2); break;
        case RectBottomLeftRule: newNode = findPositionForNewNodeBottomLeft(width, height, score1, score2); break;
        case RectContactPointRule: newNode = findPositionForNewNodeContactPoint(width, height, score1); break;
        case RectBestLongSideFit: newNode = findPositionForNewNodeBestLongSideFit(width, height, score2, score1); break;
        case RectBestAreaFit: newNode = findPositionForNewNodeBestAreaFit(width, height, score1, score2); break;
    }

    if (newNode.height == 0)
        return newNode;

    int numRectanglesToProcess = freeRectangles.count();
    for (int i = 0; i < numRectanglesToProcess; ++i)
    {
        if (splitFreeNode(freeRectangles[i], newNode))
        {
            freeRectangles.removeAt(i);
            --i;
            --numRectanglesToProcess;
        }
    }

    pruneFreeList();

    usedRectangles.append(newNode);
    return newNode;
}

/// Computes the ratio of used surface area to the total bin area.
float MaxRectsBinPack::occupancy()
{
    long usedSurfaceArea = 0;
    for (int i = 0; i < usedRectangles.count(); ++i)
        usedSurfaceArea += usedRectangles[i].width * usedRectangles[i].height;

    return (float)usedSurfaceArea / (float)(binWidth * binHeight);
}

int MaxRectsBinPack::wastedBinArea()
{
    long usedSurfaceArea = 0;
    for (int i = 0; i < usedRectangles.count(); ++i)
        usedSurfaceArea += usedRectangles[i].width * usedRectangles[i].height;

    return (int)((long)(binWidth * binHeight) - usedSurfaceArea);
}

/// Computes the placement score for placing the given rectangle with the given method.
/// @param score1 [out] The primary placement score will be outputted here.
/// @param score2 [out] The secondary placement score will be outputted here. This isu sed to break ties.
/// @return This struct identifies where the rectangle would be placed if it were placed.
Rect MaxRectsBinPack::scoreRect(int width, int height, FreeRectChoiceHeuristic method, int &score1, int &score2)
{
    Rect newNode;
    score1 = std::numeric_limits<int>::max();
    score2 = std::numeric_limits<int>::max();
    switch (method)
    {
        case RectBestShortSideFit: newNode = findPositionForNewNodeBestShortSideFit(width, height, score1, score2); break;
        case RectBottomLeftRule: newNode = findPositionForNewNodeBottomLeft(width, height, score1, score2); break;
        case RectContactPointRule: newNode = findPositionForNewNodeContactPoint(width, height, score1);
            score1 = -score1; // Reverse since we are minimizing, but for contact point score bigger is better.
            break;
        case RectBestLongSideFit: newNode = findPositionForNewNodeBestLongSideFit(width, height, score2, score1); break;
        case RectBestAreaFit: newNode = findPositionForNewNodeBestAreaFit(width, height, score1, score2); break;
    }

    // Cannot fit the current rectangle.
    if (newNode.height == 0)
    {
        score1 = std::numeric_limits<int>::max();
        score2 = std::numeric_limits<int>::max();
    }

    return newNode;
}

/// Places the given rectangle into the bin.
void MaxRectsBinPack::placeRect(Rect node)
{
    int numRectanglesToProcess = freeRectangles.count();
    for (int i = 0; i < numRectanglesToProcess; ++i)
    {
        if (splitFreeNode(freeRectangles.at(i), node))
        {
            freeRectangles.removeAt(i);
            --i;
            --numRectanglesToProcess;
        }
    }

    pruneFreeList();

    usedRectangles.append(node);
}

/// Computes the placement score for the -CP variant.
int MaxRectsBinPack::contactPointScoreNode(int x, int y, int width, int height)
{
    int score = 0;

    if (x == 0 || x + width == binWidth)
        score += height;
    if (y == 0 || y + height == binHeight)
        score += width;

    for (int i = 0; i < usedRectangles.count(); ++i)
    {
        if (usedRectangles[i].x == x + width || usedRectangles[i].x + usedRectangles[i].width == x)
            score += commonIntervalLength(usedRectangles[i].y, usedRectangles[i].y + usedRectangles[i].height, y, y + height);
        if (usedRectangles[i].y == y + height || usedRectangles[i].y + usedRectangles[i].height == y)
            score += commonIntervalLength(usedRectangles[i].x, usedRectangles[i].x + usedRectangles[i].width, x, x + width);
    }
    return score;
}

Rect MaxRectsBinPack::findPositionForNewNodeBottomLeft(int width, int height, int &bestY, int &bestX)
{
    Rect bestNode;

    bestY = std::numeric_limits<int>::max();

    for (int i = 0; i < freeRectangles.count(); ++i)
    {
        // Try to place the rectangle in upright (non-flipped) orientation.
        if (freeRectangles[i].width >= width && freeRectangles[i].height >= height)
        {
            int topSideY = freeRectangles[i].y + height;
            if (topSideY < bestY || (topSideY == bestY && freeRectangles[i].x < bestX))
            {
                bestNode.x = freeRectangles[i].x;
                bestNode.y = freeRectangles[i].y;
                bestNode.width = width;
                bestNode.height = height;
                bestY = topSideY;
                bestX = freeRectangles[i].x;
            }
        }
        if (allowRotation && freeRectangles[i].width >= height && freeRectangles[i].height >= width)
        {
            int topSideY = freeRectangles[i].y + width;
            if (topSideY < bestY || (topSideY == bestY && freeRectangles[i].x < bestX))
            {
                bestNode.x = freeRectangles[i].x;
                bestNode.y = freeRectangles[i].y;
                bestNode.width = height;
                bestNode.height = width;
                bestY = topSideY;
                bestX = freeRectangles[i].x;
            }
        }
    }
    return bestNode;
}

Rect MaxRectsBinPack::findPositionForNewNodeBestShortSideFit(int width, int height, int &bestShortSideFit, int &bestLongSideFit)
{
    Rect bestNode;
    //memset(&bestNode, 0, sizeof(Rect));

    bestShortSideFit = std::numeric_limits<int>::max();

    for (int i = 0; i < freeRectangles.count(); ++i)
    {
        // Try to place the rectangle in upright (non-flipped) orientation.
        if (freeRectangles[i].width >= width && freeRectangles[i].height >= height)
        {
            int leftoverHoriz = std::abs(freeRectangles[i].width - width);
            int leftoverVert = std::abs(freeRectangles[i].height - height);
            int shortSideFit = std::min(leftoverHoriz, leftoverVert);
            int longSideFit = std::max(leftoverHoriz, leftoverVert);

            if (shortSideFit < bestShortSideFit || (shortSideFit == bestShortSideFit && longSideFit < bestLongSideFit))
            {
                bestNode.x = freeRectangles[i].x;
                bestNode.y = freeRectangles[i].y;
                bestNode.width = width;
                bestNode.height = height;
                bestShortSideFit = shortSideFit;
                bestLongSideFit = longSideFit;
            }
        }

        if (allowRotation && freeRectangles[i].width >= height && freeRectangles[i].height >= width)
        {
            int flippedLeftoverHoriz = std::abs(freeRectangles[i].width - height);
            int flippedLeftoverVert = std::abs(freeRectangles[i].height - width);
            int flippedShortSideFit = std::min(flippedLeftoverHoriz, flippedLeftoverVert);
            int flippedLongSideFit = std::max(flippedLeftoverHoriz, flippedLeftoverVert);

            if (flippedShortSideFit < bestShortSideFit || (flippedShortSideFit == bestShortSideFit && flippedLongSideFit < bestLongSideFit))
            {
                bestNode.x = freeRectangles[i].x;
                bestNode.y = freeRectangles[i].y;
                bestNode.width = height;
                bestNode.height = width;
                bestShortSideFit = flippedShortSideFit;
                bestLongSideFit = flippedLongSideFit;
            }
        }
    }
    return bestNode;
}

Rect MaxRectsBinPack::findPositionForNewNodeBestLongSideFit(int width, int height, int &bestShortSideFit, int &bestLongSideFit)
{
    Rect bestNode;
    bestLongSideFit = std::numeric_limits<int>::max();

    for (int i = 0; i < freeRectangles.count(); ++i)
    {
        // Try to place the rectangle in upright (non-flipped) orientation.
        if (freeRectangles[i].width >= width && freeRectangles[i].height >= height)
        {
            int leftoverHoriz = std::abs(freeRectangles[i].width - width);
            int leftoverVert = std::abs(freeRectangles[i].height - height);
            int shortSideFit = std::min(leftoverHoriz, leftoverVert);
            int longSideFit = std::max(leftoverHoriz, leftoverVert);

            if (longSideFit < bestLongSideFit || (longSideFit == bestLongSideFit && shortSideFit < bestShortSideFit))
            {
                bestNode.x = freeRectangles[i].x;
                bestNode.y = freeRectangles[i].y;
                bestNode.width = width;
                bestNode.height = height;
                bestShortSideFit = shortSideFit;
                bestLongSideFit = longSideFit;
            }
        }

        if (allowRotation && freeRectangles[i].width >= height && freeRectangles[i].height >= width)
        {
            int leftoverHoriz =std::abs(freeRectangles[i].width - height);
            int leftoverVert = std::abs(freeRectangles[i].height - width);
            int shortSideFit = std::min(leftoverHoriz, leftoverVert);
            int longSideFit = std::max(leftoverHoriz, leftoverVert);

            if (longSideFit < bestLongSideFit || (longSideFit == bestLongSideFit && shortSideFit < bestShortSideFit))
            {
                bestNode.x = freeRectangles[i].x;
                bestNode.y = freeRectangles[i].y;
                bestNode.width = height;
                bestNode.height = width;
                bestShortSideFit = shortSideFit;
                bestLongSideFit = longSideFit;
            }
        }
    }
    return bestNode;
}

Rect MaxRectsBinPack::findPositionForNewNodeBestAreaFit(int width, int height, int &bestAreaFit, int &bestShortSideFit)
{
    Rect bestNode;

    bestAreaFit = std::numeric_limits<int>::max();

    for (int i = 0; i < freeRectangles.count(); ++i)
    {
        int areaFit = freeRectangles[i].width * freeRectangles[i].height - width * height;

        // Try to place the rectangle in upright (non-flipped) orientation.
        if (freeRectangles[i].width >= width && freeRectangles[i].height >= height)
        {
            int leftoverHoriz = std::abs(freeRectangles[i].width - width);
            int leftoverVert = std::abs(freeRectangles[i].height - height);
            int shortSideFit = std::min(leftoverHoriz, leftoverVert);

            if (areaFit < bestAreaFit || (areaFit == bestAreaFit && shortSideFit < bestShortSideFit))
            {
                bestNode.x = freeRectangles[i].x;
                bestNode.y = freeRectangles[i].y;
                bestNode.width = width;
                bestNode.height = height;
                bestShortSideFit = shortSideFit;
                bestAreaFit = areaFit;
            }
        }

        if (allowRotation && freeRectangles[i].width >= height && freeRectangles[i].height >= width)
        {
            int leftoverHoriz = std::abs(freeRectangles[i].width - height);
            int leftoverVert = std::abs(freeRectangles[i].height - width);
            int shortSideFit = std::min(leftoverHoriz, leftoverVert);

            if (areaFit < bestAreaFit || (areaFit == bestAreaFit && shortSideFit < bestShortSideFit))
            {
                bestNode.x = freeRectangles[i].x;
                bestNode.y = freeRectangles[i].y;
                bestNode.width = height;
                bestNode.height = width;
                bestShortSideFit = shortSideFit;
                bestAreaFit = areaFit;
            }
        }
    }
    return bestNode;
}

Rect MaxRectsBinPack::findPositionForNewNodeContactPoint(int width, int height, int &bestContactScore)
{
    Rect bestNode;
    bestContactScore = -1;

    for (int i = 0; i < freeRectangles.count(); ++i)
    {
        // Try to place the rectangle in upright (non-flipped) orientation.
        if (freeRectangles[i].width >= width && freeRectangles[i].height >= height)
        {
            int score = contactPointScoreNode(freeRectangles[i].x, freeRectangles[i].y, width, height);
            if (score > bestContactScore)
            {
                bestNode.x = freeRectangles[i].x;
                bestNode.y = freeRectangles[i].y;
                bestNode.width = width;
                bestNode.height = height;
                bestContactScore = score;
            }
        }
        if (allowRotation && freeRectangles[i].width >= height && freeRectangles[i].height >= width)
        {
            int score = contactPointScoreNode(freeRectangles[i].x, freeRectangles[i].y, width, height);
            if (score > bestContactScore)
            {
                bestNode.x = freeRectangles[i].x;
                bestNode.y = freeRectangles[i].y;
                bestNode.width = height;
                bestNode.height = width;
                bestContactScore = score;
            }
        }
    }
    return bestNode;
}

/// @return True if the free node was split.
bool MaxRectsBinPack::splitFreeNode(Rect freeNode, Rect usedNode)
{
    // Test with SAT if the rectangles even intersect.
    if (usedNode.x >= freeNode.x + freeNode.width || usedNode.x + usedNode.width <= freeNode.x ||
        usedNode.y >= freeNode.y + freeNode.height || usedNode.y + usedNode.height <= freeNode.y)
        return false;

    if (usedNode.x < freeNode.x + freeNode.width && usedNode.x + usedNode.width > freeNode.x)
    {
        // New node at the top side of the used node.
        if (usedNode.y > freeNode.y && usedNode.y < freeNode.y + freeNode.height)
        {
            Rect newNode = freeNode.copy();
            newNode.height = usedNode.y - newNode.y;
            freeRectangles.append(newNode);
        }

        // New node at the bottom side of the used node.
        if (usedNode.y + usedNode.height < freeNode.y + freeNode.height)
        {
            Rect newNode = freeNode.copy();
            newNode.y = usedNode.y + usedNode.height;
            newNode.height = freeNode.y + freeNode.height - (usedNode.y + usedNode.height);
            freeRectangles.append(newNode);
        }
    }

    if (usedNode.y < freeNode.y + freeNode.height && usedNode.y + usedNode.height > freeNode.y)
    {
        // New node at the left side of the used node.
        if (usedNode.x > freeNode.x && usedNode.x < freeNode.x + freeNode.width)
        {
            Rect newNode = freeNode.copy();
            newNode.width = usedNode.x - newNode.x;
            freeRectangles.append(newNode);
        }

        // New node at the right side of the used node.
        if (usedNode.x + usedNode.width < freeNode.x + freeNode.width)
        {
            Rect newNode = freeNode.copy();
            newNode.x = usedNode.x + usedNode.width;
            newNode.width = freeNode.x + freeNode.width - (usedNode.x + usedNode.width);
            freeRectangles.append(newNode);
        }
    }

    return true;
}

/// Goes through the free rectangle list and removes any redundant entries.
void MaxRectsBinPack::pruneFreeList()
{
    /*
    ///  Would be nice to do something like this, to avoid a Theta(n^2) loop through each pair.
    ///  But unfortunately it doesn't quite cut it, since we also want to detect containment.
    ///  Perhaps there's another way to do this faster than Theta(n^2).

    if (freeRectangles.size() > 0)
        clb::sort::QuickSort(&freeRectangles[0], freeRectangles.size(), NodeSortCmp);

    for(size_t i = 0; i < freeRectangles.size()-1; ++i)
        if (freeRectangles[i].x == freeRectangles[i+1].x &&
            freeRectangles[i].y == freeRectangles[i+1].y &&
            freeRectangles[i].width == freeRectangles[i+1].width &&
            freeRectangles[i].height == freeRectangles[i+1].height)
        {
            freeRectangles.erase(freeRectangles.begin() + i);
            --i;
        }
    */

    /// Go through each pair and remove any rectangle that is redundant.
    for (int i = 0; i < freeRectangles.count(); ++i)
    {
        for (int j = i + 1; j < freeRectangles.count(); ++j)
        {
            if (Rect::isContainedIn(freeRectangles[i], freeRectangles.at(j)))
            {
                freeRectangles.removeAt(i);
                --i;
                break;
            }
            if (Rect::isContainedIn(freeRectangles.at(j), freeRectangles[i]))
            {
                freeRectangles.removeAt(j);
                --j;
            }
        }
    }
}

/// Returns 0 if the two intervals i1 and i2 are disjoint, or the length of their overlap otherwise.
int MaxRectsBinPack::commonIntervalLength(int i1start, int i1end, int i2start, int i2end)
{
    if (i1end < i2start || i2end < i1start)
        return 0;
    return std::min(i1end, i2end) - std::max(i1start, i2start);
}
