package com.example.jamkkaebi.friend.spi;

/**
 * 친구에게 보여 줄 전시관의 모습을 만든다. <b>전시관 도메인이 구현한다.</b>
 *
 * <p>구현 빈이 아직 없으면 스냅샷을 찍지 않고, 친구 전시관은 빈 전시관으로 보인다.
 */
public interface ExhibitionViewReader {

    // 사용자의 전시관을 친구 공개용으로 만든다.
    ExhibitionView readPublicView(Long userId);
}
