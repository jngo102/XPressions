#ifndef MAX_RECTS_BIN_PACK_HPP
#define MAX_RECTS_BIN_PACK_HPP

#include <QList>
#include "atlas_rect.hpp"

/// Specifies the different heuristic rules that can be used when deciding where to place a new rectangle.
enum FreeRectChoiceHeuristic
{
    RectBestShortSideFit, /// -BSSF: Positions the rectangle against the short side of a free rectangle into which it fits the best.
    RectBestLongSideFit, /// -BLSF: Positions the rectangle against the long side of a free rectangle into which it fits the best.
    RectBestAreaFit, /// -BAF: Positions the rectangle into the smallest free rect into which it fits.
    RectBottomLeftRule, /// -BL: Does the Tetris placement.
    RectContactPointRule /// -CP: Choosest the placement where the rectangle touches other rects as much as possible.
};

class MaxRectsBinPack
{
public:
    MaxRectsBinPack();
    MaxRectsBinPack(int width, int height, bool allowRotation);

    bool insert(QList<RectSize> rects, FreeRectChoiceHeuristic method);
    QList<Rect> getMapped();
    Rect insert(int width, int height, FreeRectChoiceHeuristic method);
    float occupancy();
    int wastedBinArea();
    Rect scoreRect(int width, int height, FreeRectChoiceHeuristic method, int &score1, int &score2);
    void placeRect(Rect node);
    int contactPointScoreNode(int x, int y, int width, int height);
    Rect findPositionForNewNodeBottomLeft(int width, int height, int &bestY, int &bestX);
    Rect findPositionForNewNodeBestShortSideFit(int width, int height, int &bestShortSideFit, int &bestLongSideFit);
    Rect findPositionForNewNodeBestLongSideFit(int width, int height, int &bestShortSideFit, int &bestLongSideFit);
    Rect findPositionForNewNodeBestAreaFit(int width, int height, int &bestAreaFit, int &bestShortSideFit);
    Rect findPositionForNewNodeContactPoint(int width, int height, int &bestContactScore);
    bool splitFreeNode(Rect freeNode, Rect usedNode);
    void pruneFreeList();
    int commonIntervalLength(int i1start, int i1end, int i2start, int i2end);

    bool        allowRotation = false;
    int         binWidth = 0;
    int         binHeight = 0;

    QList<Rect> usedRectangles;
    QList<Rect> freeRectangles;
};

#endif // MAX_RECTS_BIN_PACK_HPP
