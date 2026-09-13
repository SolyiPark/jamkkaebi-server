package com.example.jamkkaebi.support;

import com.example.jamkkaebi.friend.spi.ExhibitionView;
import com.example.jamkkaebi.friend.spi.ExhibitionViewReader;

import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * 전시관 도메인 대역. 테스트가 "지금 이 사용자의 전시관은 이렇다"를 정해 두면 스냅샷이 그걸 찍는다.
 */
public class FakeExhibitionViewReader implements ExhibitionViewReader {

    public static final ExhibitionView.Grid GRID = new ExhibitionView.Grid(20, 20);

    private final Map<Long, ExhibitionView> views = new ConcurrentHashMap<>();

    public void show(Long userId, ExhibitionView view) {
        views.put(userId, view);
    }

    public void clear() {
        views.clear();
    }

    @Override
    public ExhibitionView readPublicView(Long userId) {
        return views.getOrDefault(userId, new ExhibitionView(GRID, List.of(), List.of()));
    }
}
