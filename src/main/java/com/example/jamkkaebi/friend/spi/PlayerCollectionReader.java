package com.example.jamkkaebi.friend.spi;

import java.util.Collection;
import java.util.Map;

/**
 * 사용자들의 도감 진행도를 읽는다.
 *
 * <p>친구 카드의 수집률·시대별 복원 수가 여기서 나온다. 구현 빈이 아직 없으면 모두 0으로 보인다.
 */
public interface PlayerCollectionReader {

    /**
     * @return 사용자 id → 진행도. 진행도가 없는 사용자는 빠져도 된다.
     */
    Map<Long, PlayerCollection> readAll(Collection<Long> userIds);
}
